using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HearthGate.Contracts;

namespace HearthGate.Agent;

// Owns: checking command parameters, rendering templates and sending one command through the game's channel
// (rcon, http or script). Only commands from GameCommandCatalog can run; values are checked before use.
public sealed class GameCommandRunner(GameCommandCatalog catalog, IHttpClientFactory httpFactory, ControlScriptRunner scripts, ILogger<GameCommandRunner> log)
{
    public const int DefaultMaxLength = 200;
    private const int MaxOutput = 4000;
    private static readonly Regex Placeholder = new(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    public async Task<GameCommandResult> Run(GameConfig g, GameCommandConfig cmd, IReadOnlyDictionary<string, string>? input, CancellationToken ct)
    {
        var channel = catalog.For(g).Channel;
        if (channel?.Type is not (ChannelTypes.Rcon or ChannelTypes.Http or ChannelTypes.Script))
            return new GameCommandResult(false, "This server has no command channel.", null);
        var (values, error) = Check(cmd, channel.Type, input);
        if (error is not null) return new GameCommandResult(false, error, null);
        try
        {
            var timeout = TimeSpan.FromSeconds(Math.Clamp(channel.TimeoutSeconds ?? 15, 3, 300));
            return channel.Type switch
            {
                ChannelTypes.Rcon => Ok(await Rcon.Send(channel.Address ?? "", Password(channel), Line(cmd.Template, values), timeout, ct)),
                ChannelTypes.Http => await Http(channel, cmd, values, timeout, ct),
                _ => await Script(channel, Line(cmd.Template, values), timeout, ct),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogWarning("Command {Command} on {Key} failed: {Message}", cmd.Id, g.Key, ex.Message);
            return new GameCommandResult(false, ex is RconAuthException ? "The game server refused the RCON password." : "The game server did not accept the command. See the agent log.", null);
        }
    }

    // Returns the checked values, or an error text for the user.
    public static (Dictionary<string, string> Values, string? Error) Check(GameCommandConfig cmd, string channelType, IReadOnlyDictionary<string, string>? input)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in cmd.Params)
        {
            var label = p.Label ?? p.Name;
            var v = (input is not null && input.TryGetValue(p.Name, out var raw) ? raw : null)?.Trim();
            if (string.IsNullOrEmpty(v)) v = p.Default ?? "";
            if (v.Length == 0)
            {
                if (p.Required) return (values, $"{label} is required.");
                values[p.Name] = "";
                continue;
            }
            if (v.Any(char.IsControl)) return (values, $"{label} contains a character that is not permitted.");
            switch (p.Type)
            {
                case ParamTypes.Int:
                    if (!long.TryParse(v, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n))
                        return (values, $"{label} must be a whole number.");
                    if (n < (p.Min ?? long.MinValue) || n > (p.Max ?? long.MaxValue))
                        return (values, $"{label} must be from {p.Min?.ToString() ?? "any"} to {p.Max?.ToString() ?? "any"}.");
                    v = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case ParamTypes.Choice:
                    if (p.Options is null || !p.Options.Contains(v, StringComparer.Ordinal)) return (values, $"{label} has a value that is not in the list.");
                    break;
                default:
                    if (v.Length > (p.MaxLength ?? DefaultMaxLength)) return (values, $"{label} is too long.");
                    if (p.Pattern is { Length: > 0 } && !Regex.IsMatch(v, $"^(?:{p.Pattern})$", RegexOptions.None, TimeSpan.FromSeconds(1)))
                        return (values, $"{label} does not have the correct format.");
                    // Source-engine consoles run each part of "a; b" as a separate command.
                    if (channelType != ChannelTypes.Http && v.Contains(';')) return (values, $"{label} cannot contain a semicolon.");
                    break;
            }
            values[p.Name] = v;
        }
        return (values, null);
    }

    public static string Render(string? template, IReadOnlyDictionary<string, string> values, Func<string, string> escape) =>
        Placeholder.Replace(template ?? "", m => values.TryGetValue(m.Groups[1].Value, out var v) ? escape(v) : m.Value);

    // One console line. Empty optional values leave no double spaces.
    public static string Line(string? template, IReadOnlyDictionary<string, string> values) =>
        Regex.Replace(Render(template, values, v => v), " {2,}", " ").Trim();

    private async Task<GameCommandResult> Http(GameChannelConfig channel, GameCommandConfig cmd, Dictionary<string, string> values, TimeSpan timeout, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(channel.Url)) throw new InvalidOperationException("The http channel has no url.");
        var path = Render(cmd.Path, values, Uri.EscapeDataString);
        using var req = new HttpRequestMessage(new HttpMethod(cmd.Method.ToUpperInvariant()), channel.Url.TrimEnd('/') + "/" + path.TrimStart('/'));
        var pw = Password(channel);
        switch (channel.Auth?.ToLowerInvariant())
        {
            case "basic":
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{channel.Username ?? "admin"}:{pw}")));
                break;
            case "bearer":
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pw);
                break;
        }
        if (cmd.Body is { Length: > 0 })
            req.Content = new StringContent(Render(cmd.Body, values, v => JsonEncodedText.Encode(v).ToString()), Encoding.UTF8, "application/json");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        using var resp = await httpFactory.CreateClient("game").SendAsync(req, cts.Token);
        var text = await resp.Content.ReadAsStringAsync(cts.Token);
        if (!resp.IsSuccessStatusCode) return new GameCommandResult(false, $"The game server answered with HTTP {(int)resp.StatusCode}.", Clean(text));
        try { if (text.Length > 0 && text.TrimStart()[0] is '{' or '[') text = JsonSerializer.Serialize(JsonDocument.Parse(text).RootElement, Indented); }
        catch (JsonException) { }
        return Ok(text);
    }

    private async Task<GameCommandResult> Script(GameChannelConfig channel, string text, TimeSpan timeout, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(channel.Script) || string.IsNullOrWhiteSpace(channel.ScriptServer)) throw new InvalidOperationException("The script channel needs script and scriptServer.");
        var (code, output, error) = await scripts.Command(channel.Script, channel.ScriptServer, text, timeout, ct);
        if (code != 0)
        {
            log.LogWarning("Command script failed ({Code}): {Error}", code, error);
            return new GameCommandResult(false, "The control script reported an error.", Clean(output));
        }
        return Ok(output);
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static GameCommandResult Ok(string output) => new(true, "The command was sent.", Clean(output));

    // Removes Minecraft color codes and limits the size.
    private static string? Clean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = Regex.Replace(s, "§.", "").Trim();
        return s.Length <= MaxOutput ? s : s[..MaxOutput] + "…";
    }

    private static string Password(GameChannelConfig channel)
    {
        string? pw = null;
        if (!string.IsNullOrWhiteSpace(channel.PasswordEnv))
        {
            pw = Environment.GetEnvironmentVariable(channel.PasswordEnv);
            if (string.IsNullOrEmpty(pw) && OperatingSystem.IsWindows())
                pw = Environment.GetEnvironmentVariable(channel.PasswordEnv, EnvironmentVariableTarget.User);
        }
        if (string.IsNullOrEmpty(pw) && channel.PasswordIni is { } ini && File.Exists(ini))
        {
            var key = Regex.Escape(channel.PasswordIniKey ?? "AdminPassword");
            var m = Regex.Match(File.ReadAllText(ini), key + "\\s*=\\s*(?:\"([^\"]*)\"|([^,\\r\\n)]*))");
            if (m.Success) pw = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value.Trim();
        }
        return pw ?? "";
    }
}
