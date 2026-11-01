using System.Diagnostics;
using System.Text.Json;
using HearthGate.Contracts;

namespace HearthGate.Agent;

// Owns: calling an existing game-server control script (kind: script), for example
// D:\scripts\GameServerControl.ps1 on MIKO. The script does the real start/stop work.
// Only the values from agent.yaml are passed: -Server <scriptServer> and Status/Start/Stop/Restart.
// Contract: "-Action Status -AsJson" prints a JSON array of { Server, Running, SupervisorRunning }.
public sealed class ControlScriptRunner(ILogger<ControlScriptRunner> log)
{
    private readonly SemaphoreSlim _statusLock = new(1, 1);
    private readonly Dictionary<string, (DateTime At, Dictionary<string, string> States)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<string> State(GameConfig g, CancellationToken ct)
    {
        var states = await AllStates(g.Script!, ct);
        return states.TryGetValue(g.ScriptServer!, out var s) ? s : GameStates.Unknown;
    }

    public async Task<GameActionResult> Act(GameConfig g, string action, CancellationToken ct)
    {
        var scriptAction = action switch
        {
            GameActions.Start => "Start",
            GameActions.Stop => "Stop",
            GameActions.Restart => "Restart",
            _ => null,
        };
        if (scriptAction is null) return new GameActionResult(false, "Unknown action.");
        var (code, _, error) = await Run(g.Script!, ["-Server", g.ScriptServer!, "-Action", scriptAction, "-AsJson"],
            TimeSpan.FromSeconds(Math.Clamp(g.StopTimeoutSeconds, 30, 600)), ct);
        lock (_cache) _cache.Remove(g.Script!);
        if (code != 0)
        {
            log.LogWarning("Control script {Action} {Server} failed ({Code}): {Error}", scriptAction, g.ScriptServer, code, error);
            return new GameActionResult(false, "The action failed. See the agent host log.");
        }
        return new GameActionResult(true, action switch
        {
            GameActions.Start => "The server is starting.",
            GameActions.Stop => "The server stopped.",
            _ => "The server restarted.",
        });
    }

    private async Task<Dictionary<string, string>> AllStates(string script, CancellationToken ct)
    {
        await _statusLock.WaitAsync(ct);
        try
        {
            lock (_cache)
                if (_cache.TryGetValue(script, out var c) && DateTime.UtcNow - c.At < TimeSpan.FromSeconds(5)) return c.States;
            var (code, output, error) = await Run(script, ["-Action", "Status", "-AsJson"], TimeSpan.FromSeconds(60), ct);
            if (code != 0) throw new InvalidOperationException($"Status failed ({code}): {error}");
            var states = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var doc = JsonDocument.Parse(output);
            var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : [doc.RootElement];
            foreach (var e in items)
            {
                var name = e.GetProperty("Server").GetString() ?? "";
                var running = e.TryGetProperty("Running", out var r) && r.ValueKind == JsonValueKind.True;
                var supervisor = e.TryGetProperty("SupervisorRunning", out var s) && s.ValueKind == JsonValueKind.True;
                states[name] = running ? GameStates.Running : supervisor ? GameStates.Starting : GameStates.Stopped;
            }
            lock (_cache) _cache[script] = (DateTime.UtcNow, states);
            return states;
        }
        finally { _statusLock.Release(); }
    }

    private static async Task<(int Code, string Output, string Error)> Run(string script, string[] args, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(script)!,
        };
        foreach (var a in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }.Concat(args))
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { p.Kill(entireProcessTree: false); } catch { }
            return (-1, "", "Timed out.");
        }
        return (p.ExitCode, await stdout, (await stderr).Trim());
    }
}
