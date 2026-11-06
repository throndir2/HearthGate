using System.Collections.Concurrent;
using System.Text.Json;
using HearthGate.Contracts;

namespace HearthGate.Agent;

// Owns: per-container resource statistics (CPU, memory, network, disk) from the Docker stats API. Read-only.
// Uses one-shot stats (fast, no 1 s wait) and computes CPU and rates from the previous sample of the same container,
// so the first call after a start returns memory only. Called by HostReader on each /v1/host request.
public sealed class ContainerStatsReader(AgentConfig config, IHttpClientFactory httpFactory)
{
    private sealed record Sample(DateTimeOffset At, ulong Cpu, ulong System, long Rx, long Tx, long Read, long Write);

    private readonly ConcurrentDictionary<string, Sample> _previous = new();

    public async Task<Dictionary<string, ContainerStats>> Read(IReadOnlyList<string> runningIds, CancellationToken ct)
    {
        var client = httpFactory.CreateClient("docker");
        var baseUrl = DockerHttp.BaseUrl(config.Docker!.Endpoint);
        using var gate = new SemaphoreSlim(8);
        var results = await Task.WhenAll(runningIds.Select(async id =>
        {
            await gate.WaitAsync(ct);
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(8));
                using var doc = JsonDocument.Parse(await client.GetStringAsync($"{baseUrl}/containers/{id}/stats?stream=false&one-shot=true", cts.Token));
                return (id, stats: Parse(id, doc.RootElement));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { return (id, stats: (ContainerStats?)null); }
            finally { gate.Release(); }
        }));
        foreach (var gone in _previous.Keys.Except(runningIds)) _previous.TryRemove(gone, out _);
        return results.Where(r => r.stats is not null).ToDictionary(r => r.id, r => r.stats!);
    }

    private ContainerStats Parse(string id, JsonElement s)
    {
        var now = DateTimeOffset.UtcNow;
        var cpu = U64(s, "cpu_stats", "cpu_usage", "total_usage");
        var system = U64(s, "cpu_stats", "system_cpu_usage");

        // Same as "docker stats": usage minus inactive page cache (cgroup v2: inactive_file, v1: total_inactive_file).
        var usage = (long)U64(s, "memory_stats", "usage");
        var inactive = (long)Math.Max(U64(s, "memory_stats", "stats", "inactive_file"), U64(s, "memory_stats", "stats", "total_inactive_file"));
        var memory = inactive < usage ? usage - inactive : usage;
        var limit = (long)U64(s, "memory_stats", "limit");

        long rx = 0, tx = 0;
        if (s.TryGetProperty("networks", out var nets) && nets.ValueKind == JsonValueKind.Object)
            foreach (var n in nets.EnumerateObject()) { rx += (long)U64(n.Value, "rx_bytes"); tx += (long)U64(n.Value, "tx_bytes"); }

        long read = 0, write = 0;
        if (s.TryGetProperty("blkio_stats", out var blk) && blk.TryGetProperty("io_service_bytes_recursive", out var io) && io.ValueKind == JsonValueKind.Array)
            foreach (var e in io.EnumerateArray())
            {
                var op = e.TryGetProperty("op", out var o) ? o.GetString() : null;
                var v = (long)U64(e, "value");
                if (string.Equals(op, "read", StringComparison.OrdinalIgnoreCase)) read += v;
                else if (string.Equals(op, "write", StringComparison.OrdinalIgnoreCase)) write += v;
            }

        var current = new Sample(now, cpu, system, rx, tx, read, write);
        double? cpuPct = null, rxRate = null, txRate = null, readRate = null, writeRate = null;
        if (_previous.TryGetValue(id, out var p) && system > p.System && cpu >= p.Cpu)
        {
            // Share of the whole host (all cores together), 0-100.
            cpuPct = Math.Round(100.0 * (cpu - p.Cpu) / (system - p.System), 2);
            var secs = (now - p.At).TotalSeconds;
            if (secs > 0.5)
            {
                rxRate = Rate(rx, p.Rx, secs); txRate = Rate(tx, p.Tx, secs);
                readRate = Rate(read, p.Read, secs); writeRate = Rate(write, p.Write, secs);
            }
        }
        _previous[id] = current;
        return new ContainerStats(cpuPct, memory, limit > 0 ? limit : null, rxRate, txRate, readRate, writeRate);
    }

    private static double? Rate(long now, long before, double secs) => now >= before ? Math.Round((now - before) / secs) : null;

    private static ulong U64(JsonElement e, params string[] path)
    {
        foreach (var p in path)
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(p, out e)) return 0;
        return e.ValueKind == JsonValueKind.Number && e.TryGetUInt64(out var v) ? v : 0;
    }
}
