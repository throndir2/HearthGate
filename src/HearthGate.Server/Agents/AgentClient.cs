using System.Collections.Concurrent;
using System.Net.Http.Headers;
using HearthGate.Contracts;
using HearthGate.Server.Config;

namespace HearthGate.Server.Agents;

// Owns: talking to host agents and caching their snapshots. Agent URLs and tokens come only from config/env.

public sealed record HostView(string Id, string Name, bool HasAgent, bool Online, DateTimeOffset? LastSeen, HostSnapshot? Snapshot,
    string? Address, HearthGate.Server.Status.HostReachability? Network);

public sealed class AgentClient(IHttpClientFactory httpFactory, ConfigProvider config)
{
    private HttpRequestMessage Request(HostConfig host, HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, host.AgentUrl!.TrimEnd('/') + path);
        var token = ConfigProvider.Secret(host.TokenEnv);
        if (!string.IsNullOrEmpty(token)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    private HostConfig? Host(string id) => config.Current.Hosts.FirstOrDefault(h => h.Id == id);

    public async Task<HostSnapshot?> GetHost(HostConfig host, CancellationToken ct)
    {
        using var req = Request(host, HttpMethod.Get, "/v1/host");
        using var resp = await httpFactory.CreateClient("agent").SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<HostSnapshot>(ct);
    }

    public async Task<List<GameServerStatus>> GetGames(HostConfig host, CancellationToken ct)
    {
        using var req = Request(host, HttpMethod.Get, "/v1/games");
        using var resp = await httpFactory.CreateClient("agent").SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<List<GameServerStatus>>(ct) ?? [];
    }

    public async Task<GameActionResult> GameAction(string hostId, string agentKey, string action, CancellationToken ct)
    {
        var host = Host(hostId) ?? throw new InvalidOperationException("Unknown host.");
        using var req = Request(host, HttpMethod.Post, $"/v1/games/{Uri.EscapeDataString(agentKey)}/{action}");
        using var resp = await httpFactory.CreateClient("agent-action").SendAsync(req, ct);
        var result = await resp.Content.ReadFromJsonAsync<GameActionResult>(ct);
        return result ?? new GameActionResult(false, "No answer from the agent.");
    }

    public async Task<GameCommandResult> GameCommand(string hostId, string agentKey, string commandId, Dictionary<string, string> parameters, CancellationToken ct)
    {
        var host = Host(hostId) ?? throw new InvalidOperationException("Unknown host.");
        using var req = Request(host, HttpMethod.Post, $"/v1/games/{Uri.EscapeDataString(agentKey)}/commands/{Uri.EscapeDataString(commandId)}");
        req.Content = JsonContent.Create(new GameCommandRequest(parameters));
        using var resp = await httpFactory.CreateClient("agent-action").SendAsync(req, ct);
        var result = await resp.Content.ReadFromJsonAsync<GameCommandResult>(ct);
        return result ?? new GameCommandResult(false, "No answer from the agent.", null);
    }
}

public sealed class AgentCache
{
    public ConcurrentDictionary<string, (DateTimeOffset At, HostSnapshot Snapshot)> Hosts { get; } = new();
    public ConcurrentDictionary<string, (DateTimeOffset At, GameServerStatus Status)> Games { get; } = new(); // key: hostId/agentKey
    public ConcurrentDictionary<string, DateTimeOffset> LastFailure { get; } = new();
}

public sealed class AgentPoller(ConfigProvider config, AgentClient client, AgentCache cache, ILogger<AgentPoller> log) : BackgroundService
{
    private readonly SemaphoreSlim _kick = new(0);

    public void Kick() { if (_kick.CurrentCount == 0) _kick.Release(); }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.WhenAll(config.Current.Hosts.Where(h => !string.IsNullOrWhiteSpace(h.AgentUrl)).Select(h => PollHost(h, ct)));
            try { await _kick.WaitAsync(TimeSpan.FromSeconds(20), ct); } catch (OperationCanceledException) { }
        }
    }

    private async Task PollHost(HostConfig host, CancellationToken ct)
    {
        try
        {
            var snap = await client.GetHost(host, ct);
            if (snap is not null) cache.Hosts[host.Id] = (DateTimeOffset.UtcNow, snap);
            if (config.Current.GameServers.Any(g => g.Host == host.Id))
                foreach (var g in await client.GetGames(host, ct))
                    cache.Games[$"{host.Id}/{g.Key}"] = (DateTimeOffset.UtcNow, g);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            cache.LastFailure[host.Id] = DateTimeOffset.UtcNow;
            log.LogDebug("Agent {Host} poll failed: {Message}", host.Id, ex.Message);
        }
    }
}
