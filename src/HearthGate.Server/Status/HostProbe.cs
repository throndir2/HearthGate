using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using HearthGate.Server.Config;

namespace HearthGate.Server.Status;

// Owns: network reachability of configured hosts (ping and TCP ports), for users with hosts.view.
// Needs: hosts[].address and hosts[].ports from config. Targets come only from config (no SSRF).
// Changes: HostProbeStore only. Read by GET /api/hosts.

public sealed record PortState(int Port, bool Open);

public sealed record HostReachability(bool? Reachable, int? PingMs, string? PingError, IReadOnlyList<PortState> Ports, DateTimeOffset CheckedAt, DateTimeOffset? Since);

public sealed class HostProbeStore
{
    public ConcurrentDictionary<string, HostReachability> Items { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class HostProbeService(ConfigProvider config, HostProbeStore store, ILogger<HostProbeService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var hosts = config.Current.Hosts.Where(h => !string.IsNullOrWhiteSpace(h.Address)).ToList();
            await Task.WhenAll(hosts.Select(h => Probe(h, ct)));
            try { await Task.Delay(TimeSpan.FromSeconds(30), ct); } catch (OperationCanceledException) { }
        }
    }

    private async Task Probe(HostConfig host, CancellationToken ct)
    {
        try
        {
            var address = host.Address!.Trim();
            int? pingMs = null;
            string? pingError = null;
            if (host.Ping)
            {
                try
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(address, TimeSpan.FromSeconds(2), cancellationToken: ct);
                    if (reply.Status == IPStatus.Success) pingMs = (int)reply.RoundtripTime;
                    else pingError = reply.Status.ToString();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    pingError = "Ping is not available";
                    log.LogDebug("Ping {Host} failed: {Message}", host.Id, ex.GetBaseException().Message);
                }
            }
            var ports = await Task.WhenAll(host.Ports.Distinct().Select(async p => new PortState(p, await TcpOpen(address, p, ct))));
            bool? reachable = pingMs is not null || ports.Any(p => p.Open) ? true
                : ports.Length > 0 || (host.Ping && pingError != "Ping is not available") ? false
                : null;
            var now = DateTimeOffset.UtcNow;
            store.Items.TryGetValue(host.Id, out var previous);
            var since = previous is not null && previous.Reachable == reachable ? previous.Since : now;
            store.Items[host.Id] = new HostReachability(reachable, pingMs, pingError, ports, now, since);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning("Network check of {Host} failed: {Message}", host.Id, ex.Message);
        }
    }

    private static async Task<bool> TcpOpen(string address, int port, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(address, port, cts.Token);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { return false; }
    }
}
