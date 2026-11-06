using System.Reflection;
using HearthGate.Contracts;

namespace HearthGate.Agent;

// Owns: game profiles (built-in GameProfiles/*.yaml plus profilesDir) and the resolved channel, commands
// and stop steps of each game in agent.yaml. Built once at startup; read-only after that.
// Used by GameController (status catalog, stop steps) and GameCommandRunner (sending commands).
public sealed class GameCommandCatalog
{
    public sealed record ResolvedGame(GameChannelConfig? Channel, IReadOnlyDictionary<string, GameCommandConfig> Commands, IReadOnlyList<StopStepConfig> StopSteps);

    private static readonly ResolvedGame Empty = new(null, new Dictionary<string, GameCommandConfig>(), []);
    private readonly Dictionary<string, ResolvedGame> _games = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, GameProfile> Profiles { get; }

    public GameCommandCatalog(AgentConfig config, ILogger<GameCommandCatalog> log)
    {
        var profiles = new Dictionary<string, GameProfile>(StringComparer.OrdinalIgnoreCase);
        var asm = Assembly.GetExecutingAssembly();
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("GameProfiles.", StringComparison.Ordinal)))
        {
            using var reader = new StreamReader(asm.GetManifestResourceStream(name)!);
            Add(profiles, reader.ReadToEnd(), Path.GetFileNameWithoutExtension(name["GameProfiles.".Length..]), log);
        }
        var dir = config.ProfilesDir ?? Path.Combine(config.ConfigDirectory, "game-profiles");
        if (Directory.Exists(dir))
            foreach (var file in Directory.GetFiles(dir, "*.yaml"))
                Add(profiles, File.ReadAllText(file), Path.GetFileNameWithoutExtension(file), log);
        Profiles = profiles;

        foreach (var g in config.Games)
        {
            GameProfile? profile = null;
            if (!string.IsNullOrWhiteSpace(g.Profile) && !profiles.TryGetValue(g.Profile, out profile))
                log.LogWarning("Game {Key}: unknown profile {Profile}", g.Key, g.Profile);
            _games[g.Key] = Resolve(g, profile);
        }
    }

    public ResolvedGame For(GameConfig g) => _games.TryGetValue(g.Key, out var r) ? r : Empty;

    public GameCommandConfig? Command(GameConfig g, string id) => For(g).Commands.TryGetValue(id, out var c) ? c : null;

    public IReadOnlyList<GameCommandInfo>? Info(GameConfig g)
    {
        var list = For(g).Commands.Values.Where(c => !c.Hidden).Select(c => new GameCommandInfo(
            c.Id, c.Label ?? c.Id, c.Description, c.Level, c.Confirm, c.RequiresRunning,
            [.. c.Params.Select(p => new GameCommandParamInfo(p.Name, p.Label ?? p.Name, p.Type, p.Required,
                p.Type == ParamTypes.Text ? p.MaxLength ?? GameCommandRunner.DefaultMaxLength : null,
                p.Min, p.Max, p.Options, p.Default, p.Pattern))])).ToList();
        return list.Count == 0 ? null : list;
    }

    private static void Add(Dictionary<string, GameProfile> profiles, string yaml, string fallbackId, ILogger log)
    {
        try
        {
            var p = AgentConfig.Yaml.Deserialize<GameProfile>(yaml);
            if (p is null) return;
            if (string.IsNullOrWhiteSpace(p.Id)) p.Id = fallbackId;
            profiles[p.Id] = p;
        }
        catch (Exception ex) { log.LogWarning("Game profile {Id} is not valid: {Message}", fallbackId, ex.Message); }
    }

    private static ResolvedGame Resolve(GameConfig g, GameProfile? profile)
    {
        var channel = Merge(profile?.Channel, g.Channel);
        // Existing palworldRest settings give the REST channel when the game does not set its own channel.
        if (g.PalworldRest is { } rest && channel?.Type == ChannelTypes.Http && g.Channel is null)
        {
            channel.Url = rest.Url;
            channel.PasswordEnv = rest.PasswordEnv;
            channel.PasswordIni = rest.SettingsIni ?? channel.PasswordIni;
        }
        if (channel?.Type == ChannelTypes.Script)
        {
            channel.Script ??= g.Script;
            channel.ScriptServer ??= g.ScriptServer;
        }

        var commands = new Dictionary<string, GameCommandConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in (profile?.Commands ?? []).Concat(g.Commands).Where(c => !string.IsNullOrWhiteSpace(c.Id)))
        {
            if (c.Disabled) { commands.Remove(c.Id); continue; }
            if (c.Level is not (GameCommandLevels.Control or GameCommandLevels.Admin)) c.Level = GameCommandLevels.Admin;
            commands[c.Id] = c;
        }
        return new ResolvedGame(channel, commands, g.StopSteps ?? profile?.StopSteps ?? []);
    }

    private static GameChannelConfig? Merge(GameChannelConfig? a, GameChannelConfig? b)
    {
        if (a is null && b is null) return null;
        a ??= new(); b ??= new();
        return new GameChannelConfig
        {
            Type = b.Type ?? a.Type,
            Address = b.Address ?? a.Address,
            Url = b.Url ?? a.Url,
            Auth = b.Auth ?? a.Auth,
            Username = b.Username ?? a.Username,
            PasswordEnv = b.PasswordEnv ?? a.PasswordEnv,
            PasswordIni = b.PasswordIni ?? a.PasswordIni,
            PasswordIniKey = b.PasswordIniKey ?? a.PasswordIniKey,
            Script = b.Script ?? a.Script,
            ScriptServer = b.ScriptServer ?? a.ScriptServer,
            TimeoutSeconds = b.TimeoutSeconds ?? a.TimeoutSeconds,
        };
    }
}
