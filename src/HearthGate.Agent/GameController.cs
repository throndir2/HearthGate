using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using HearthGate.Contracts;

namespace HearthGate.Agent;

// Owns: status and start/stop/restart of the game servers in agent.yaml. Nothing else can be run.
// Kinds: script (an existing control script does the work), process (exe + process names), windowsService, docker (socket proxy).
// If palworldRest is set, it is used for players/version and for a graceful save + shutdown (when no stop steps are set).
// Server commands come from GameCommandCatalog and run through GameCommandRunner; stop steps run before a stop or restart.
public sealed class GameController(AgentConfig config, IHttpClientFactory httpFactory, ControlScriptRunner scripts,
    GameCommandCatalog catalog, GameCommandRunner runner, ILogger<GameController> log)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<string, string> _transition = new();

    public async Task<List<GameServerStatus>> All(CancellationToken ct) =>
        [.. await Task.WhenAll(config.Games.Select(g => Status(g, ct)))];

    public GameConfig? Find(string key) => config.Games.FirstOrDefault(g => string.Equals(g.Key, key, StringComparison.OrdinalIgnoreCase));

    public async Task<GameServerStatus> Status(GameConfig g, CancellationToken ct)
    {
        string state;
        try { state = await RawState(g, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogDebug(ex, "Status failed"); state = GameStates.Unknown; }
        if (_transition.TryGetValue(g.Key, out var t)) state = t;
        int? players = null; List<string>? names = null; string? version = null;
        if (state == GameStates.Running && g.PalworldRest is { } rest)
        {
            try
            {
                var info = await Pal(rest, HttpMethod.Get, "/v1/api/info", null, ct);
                version = info.TryGetProperty("version", out var v) ? v.GetString() : null;
                var pl = await Pal(rest, HttpMethod.Get, "/v1/api/players", null, ct);
                if (pl.TryGetProperty("players", out var arr))
                {
                    names = arr.EnumerateArray().Select(p => p.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "").ToList();
                    players = names.Count;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogDebug("Palworld REST failed: {Message}", ex.Message); }
        }
        return new GameServerStatus(g.Key, state, players, names, version, null, catalog.Info(g));
    }

    public async Task<GameActionResult> Act(GameConfig g, string action, CancellationToken ct)
    {
        var gate = _locks.GetOrAdd(g.Key, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, ct)) return new GameActionResult(false, "Another action is running for this server.");
        try
        {
            if (g.Kind == GameKinds.Script)
            {
                _transition[g.Key] = action == GameActions.Stop ? GameStates.Stopping : GameStates.Starting;
                if (action != GameActions.Start && catalog.For(g).StopSteps.Count > 0 && await RawState(g, ct) == GameStates.Running)
                    await RunStopSteps(g, ct);
                return await scripts.Act(g, action, ct);
            }
            var state = await RawState(g, ct);
            switch (action)
            {
                case GameActions.Start:
                    if (state == GameStates.Running) return new GameActionResult(true, "The server is already running.");
                    await Start(g, ct);
                    return new GameActionResult(true, "The server is starting.");
                case GameActions.Stop:
                    if (state == GameStates.Stopped) return new GameActionResult(true, "The server is already stopped.");
                    await Stop(g, ct);
                    return new GameActionResult(true, "The server stopped.");
                case GameActions.Restart:
                    if (state != GameStates.Stopped) await Stop(g, ct);
                    await Start(g, ct);
                    return new GameActionResult(true, "The server is restarting.");
                default:
                    return new GameActionResult(false, "Unknown action.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Action {Action} on {Key} failed", action, g.Key);
            return new GameActionResult(false, "The action failed. See the agent log.");
        }
        finally
        {
            _transition.TryRemove(g.Key, out _);
            gate.Release();
        }
    }

    private async Task<string> RawState(GameConfig g, CancellationToken ct)
    {
        switch (g.Kind)
        {
            case GameKinds.Script:
                return await scripts.State(g, ct);
            case GameKinds.Process:
                return Processes(g).Count > 0 ? GameStates.Running : GameStates.Stopped;
            case GameKinds.WindowsService when OperatingSystem.IsWindows():
                using (var sc = new System.ServiceProcess.ServiceController(g.Service!))
                    return sc.Status switch
                    {
                        System.ServiceProcess.ServiceControllerStatus.Running => GameStates.Running,
                        System.ServiceProcess.ServiceControllerStatus.Stopped => GameStates.Stopped,
                        System.ServiceProcess.ServiceControllerStatus.StartPending => GameStates.Starting,
                        System.ServiceProcess.ServiceControllerStatus.StopPending => GameStates.Stopping,
                        _ => GameStates.Unknown,
                    };
            case GameKinds.Docker:
                var json = await Docker(HttpMethod.Get, $"/containers/{Uri.EscapeDataString(g.Container!)}/json", ct);
                using (var doc = System.Text.Json.JsonDocument.Parse(json))
                    return doc.RootElement.GetProperty("State").GetProperty("Running").GetBoolean() ? GameStates.Running : GameStates.Stopped;
            default:
                return GameStates.Unknown;
        }
    }

    private async Task Start(GameConfig g, CancellationToken ct)
    {
        _transition[g.Key] = GameStates.Starting;
        switch (g.Kind)
        {
            case GameKinds.Process:
                var psi = new ProcessStartInfo(g.Exe!) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = g.WorkingDir ?? Path.GetDirectoryName(g.Exe!)! };
                if (!string.IsNullOrWhiteSpace(g.Args)) psi.Arguments = g.Args;
                Process.Start(psi);
                break;
            case GameKinds.WindowsService when OperatingSystem.IsWindows():
                using (var sc = new System.ServiceProcess.ServiceController(g.Service!))
                {
                    sc.Start();
                    sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(60));
                }
                break;
            case GameKinds.Docker:
                await Docker(HttpMethod.Post, $"/containers/{Uri.EscapeDataString(g.Container!)}/start", ct);
                break;
        }
    }

    private async Task Stop(GameConfig g, CancellationToken ct)
    {
        _transition[g.Key] = GameStates.Stopping;
        if (catalog.For(g).StopSteps.Count > 0)
        {
            if (await RunStopSteps(g, ct)) return;
        }
        else if (g.PalworldRest is { } rest)
        {
            try
            {
                await Pal(rest, HttpMethod.Post, "/v1/api/save", null, ct);
                await Pal(rest, HttpMethod.Post, "/v1/api/shutdown", new { waittime = rest.ShutdownWaitSeconds, message = rest.ShutdownMessage }, ct);
                if (await WaitStopped(g, TimeSpan.FromSeconds(rest.ShutdownWaitSeconds + 45), ct)) return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning("Graceful Palworld shutdown failed: {Message}", ex.Message); }
        }
        switch (g.Kind)
        {
            case GameKinds.Process:
                foreach (var p in Processes(g)) { try { p.CloseMainWindow(); } catch { } }
                if (await WaitStopped(g, TimeSpan.FromSeconds(10), ct)) return;
                foreach (var p in Processes(g)) { try { p.Kill(entireProcessTree: true); } catch { } }
                await WaitStopped(g, TimeSpan.FromSeconds(Math.Max(5, g.StopTimeoutSeconds)), ct);
                break;
            case GameKinds.WindowsService when OperatingSystem.IsWindows():
                using (var sc = new System.ServiceProcess.ServiceController(g.Service!))
                {
                    sc.Stop();
                    sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(g.StopTimeoutSeconds));
                }
                break;
            case GameKinds.Docker:
                await Docker(HttpMethod.Post, $"/containers/{Uri.EscapeDataString(g.Container!)}/stop?t={g.StopTimeoutSeconds}", ct);
                break;
        }
    }

    public async Task<GameCommandResult> Command(GameConfig g, GameCommandConfig cmd, IReadOnlyDictionary<string, string>? input, CancellationToken ct)
    {
        if (_transition.ContainsKey(g.Key)) return new GameCommandResult(false, "The server is starting or stopping. Try again later.", null);
        if (cmd.RequiresRunning)
        {
            string state;
            try { state = await RawState(g, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogDebug(ex, "Status failed"); state = GameStates.Unknown; }
            if (state != GameStates.Running) return new GameCommandResult(false, "The server is not running.", null);
        }
        return await runner.Run(g, cmd, input, ct);
    }

    // Runs the stop steps in order. A failed step does not stop the sequence. Returns true when the server stopped by itself.
    private async Task<bool> RunStopSteps(GameConfig g, CancellationToken ct)
    {
        foreach (var step in catalog.For(g).StopSteps)
        {
            if (step.Command is { } id)
            {
                var cmd = catalog.Command(g, id);
                if (cmd is null) { log.LogWarning("Stop step for {Key}: unknown command {Command}", g.Key, id); continue; }
                var r = await runner.Run(g, cmd, step.Params, ct);
                if (!r.Ok) log.LogWarning("Stop step {Command} for {Key} failed: {Message}", id, g.Key, r.Message);
            }
            if (step.Wait is > 0) await Task.Delay(TimeSpan.FromSeconds(Math.Min(step.Wait.Value, 600)), ct);
            if (step.WaitForStop is > 0 && await WaitStopped(g, TimeSpan.FromSeconds(Math.Min(step.WaitForStop.Value, 600)), ct)) return true;
        }
        return false;
    }

    private async Task<bool> WaitStopped(GameConfig g, TimeSpan timeout, CancellationToken ct)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (await RawState(g, ct) == GameStates.Stopped) return true;
            await Task.Delay(1000, ct);
        }
        return false;
    }

    private static List<Process> Processes(GameConfig g)
    {
        var names = g.ProcessNames.Count > 0 ? g.ProcessNames : [Path.GetFileNameWithoutExtension(g.Exe ?? "")];
        var dir = g.Exe is null ? null : Path.GetDirectoryName(Path.GetFullPath(g.Exe));
        var result = new List<Process>();
        foreach (var n in names.Where(n => n.Length > 0))
            foreach (var p in Process.GetProcessesByName(n))
            {
                // When the exe is known, only match processes from that install folder.
                string? path = null;
                try { path = p.MainModule?.FileName; } catch { }
                if (dir is null || path is null || path.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) result.Add(p);
            }
        return result;
    }

    private async Task<System.Text.Json.JsonElement> Pal(PalworldRestConfig rest, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var client = httpFactory.CreateClient("game");
        using var req = new HttpRequestMessage(method, rest.Url.TrimEnd('/') + path);
        var pw = Environment.GetEnvironmentVariable(rest.PasswordEnv);
        if (string.IsNullOrEmpty(pw) && rest.SettingsIni is { } ini && File.Exists(ini))
        {
            var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(ini), "AdminPassword=\"([^\"]*)\"");
            pw = m.Success ? m.Groups[1].Value : null;
        }
        pw ??= "";
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:" + pw)));
        if (body is not null) req.Content = JsonContent.Create(body);
        using var resp = await client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var text = await resp.Content.ReadAsStringAsync(ct);
        return string.IsNullOrWhiteSpace(text) ? default : System.Text.Json.JsonDocument.Parse(text).RootElement.Clone();
    }

    private async Task<string> Docker(HttpMethod method, string path, CancellationToken ct)
    {
        if (config.Docker is not { Endpoint.Length: > 0 }) throw new InvalidOperationException("Docker is not configured.");
        using var req = new HttpRequestMessage(method, DockerHttp.BaseUrl(config.Docker.Endpoint) + path);
        using var resp = await httpFactory.CreateClient("docker").SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode && (int)resp.StatusCode != 304) resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }
}
