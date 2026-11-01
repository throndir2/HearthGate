using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using HearthGate.Server.Config;
using HearthGate.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace HearthGate.Server.Status;

// Owns: service health. Checks run only from this background service on config targets.
// Readers (guest and user APIs) only read the cached snapshot. Guests can never start a probe.

public static class ServiceState
{
    public const string Up = "up";
    public const string Degraded = "degraded";
    public const string Down = "down";
    public const string Unknown = "unknown";
}

public sealed record StatusPoint(DateTimeOffset At, string State, int? LatencyMs);

public sealed class ServiceStatus
{
    public string State { get; set; } = ServiceState.Unknown;
    public DateTimeOffset? LastChecked { get; set; }
    public DateTimeOffset? LastChange { get; set; }
    public int? LatencyMs { get; set; }
    public string? LastError { get; set; } // short class only, for users with status.details
    public int ConsecutiveFailures { get; set; }
    public List<StatusPoint> History { get; } = [];
}

public sealed class StatusStore
{
    private const int MaxHistory = 288;
    private readonly ConcurrentDictionary<string, ServiceStatus> _items = new(StringComparer.OrdinalIgnoreCase);

    public ServiceStatus Get(string id) => _items.GetOrAdd(id, _ => new ServiceStatus());

    public void Record(string id, string state, int? latency, string? error, DateTimeOffset at)
    {
        var s = Get(id);
        lock (s)
        {
            if (s.State != state) s.LastChange = at;
            s.State = state;
            s.LatencyMs = latency;
            s.LastError = error;
            s.LastChecked = at;
            s.History.Add(new StatusPoint(at, state, latency));
            if (s.History.Count > MaxHistory) s.History.RemoveRange(0, s.History.Count - MaxHistory);
        }
    }

    public void Seed(string id, IEnumerable<StatusPoint> points)
    {
        var s = Get(id);
        lock (s)
        {
            s.History.AddRange(points.TakeLast(MaxHistory));
            var last = s.History.LastOrDefault();
            if (last is not null) { s.State = last.State; s.LastChecked = last.At; s.LatencyMs = last.LatencyMs; }
        }
    }

    public double? Uptime(string id, TimeSpan window)
    {
        var s = Get(id);
        lock (s)
        {
            var since = DateTimeOffset.UtcNow - window;
            var pts = s.History.Where(p => p.At >= since && p.State != ServiceState.Unknown).ToList();
            if (pts.Count == 0) return null;
            return Math.Round(100.0 * pts.Count(p => p.State != ServiceState.Down) / pts.Count, 2);
        }
    }
}

public sealed class StatusMonitor(
    ConfigProvider config,
    StatusStore store,
    IHttpClientFactory httpFactory,
    IServiceScopeFactory scopes,
    ILogger<StatusMonitor> log) : BackgroundService
{
    private readonly Dictionary<string, DateTimeOffset> _due = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await LoadHistory(ct);
        var lastPrune = DateTimeOffset.MinValue;
        while (!ct.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var due = config.Current.Services
                .Where(s => s.Check is { Type: not "none" } || (s.Check is null && s.Url is not null))
                .Where(s => !_due.TryGetValue(s.Id, out var t) || t <= now)
                .ToList();
            foreach (var s in due) _due[s.Id] = now.AddSeconds(Math.Max(15, s.Check?.IntervalSeconds ?? 60));
            if (due.Count > 0)
            {
                var results = await Task.WhenAll(due.Select(s => CheckOne(s, ct)));
                await Persist(results, ct);
            }
            if (now - lastPrune > TimeSpan.FromHours(1)) { await Prune(ct); lastPrune = now; }
            try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch (OperationCanceledException) { }
        }
    }

    private async Task<StatusSample> CheckOne(ServiceConfig svc, CancellationToken ct)
    {
        var check = svc.Check ?? new CheckConfig();
        var target = check.Target ?? svc.Url!;
        var sw = Stopwatch.StartNew();
        string state;
        string? error = null;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(check.TimeoutSeconds, 1, 60)));
            if (check.Type == "tcp")
            {
                var (host, port) = ParseHostPort(target);
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(host, port, cts.Token);
                state = ServiceState.Up;
            }
            else
            {
                var client = httpFactory.CreateClient(check.InsecureTls ? "status-insecure" : "status");
                using var req = new HttpRequestMessage(HttpMethod.Get, target);
                using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                var code = (int)resp.StatusCode;
                var ok = check.ExpectedStatus is { Count: > 0 } ? check.ExpectedStatus.Contains(code) : code is >= 200 and < 400;
                state = ok ? ServiceState.Up : ServiceState.Degraded;
                if (!ok) error = $"HTTP {code}";
            }
            if (state == ServiceState.Up && sw.ElapsedMilliseconds > check.SlowMs) { state = ServiceState.Degraded; error = "Slow response"; }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { state = ServiceState.Down; error = "Timeout"; }
        catch (HttpRequestException ex) { state = ServiceState.Down; error = ex.HttpRequestError.ToString(); }
        catch (SocketException ex) { state = ServiceState.Down; error = ex.SocketErrorCode.ToString(); }
        catch (Exception ex) { state = ServiceState.Down; error = ex.GetType().Name; }

        var latency = (int)sw.ElapsedMilliseconds;
        var current = store.Get(svc.Id);
        if (state == ServiceState.Down)
        {
            current.ConsecutiveFailures++;
            // One failure is degraded; N in a row is down.
            if (current.ConsecutiveFailures < Math.Max(1, check.FailuresBeforeDown)) state = ServiceState.Degraded;
        }
        else current.ConsecutiveFailures = 0;

        var at = DateTimeOffset.UtcNow;
        store.Record(svc.Id, state, state == ServiceState.Down ? null : latency, error, at);
        return new StatusSample { ServiceId = svc.Id, At = at, State = state, LatencyMs = state == ServiceState.Down ? null : latency };
    }

    private static (string Host, int Port) ParseHostPort(string target)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out var u) && u.Port > 0) return (u.Host, u.Port);
        var i = target.LastIndexOf(':');
        return (target[..i], int.Parse(target[(i + 1)..]));
    }

    private async Task LoadHistory(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var since = DateTimeOffset.UtcNow.AddDays(-1);
            var rows = await db.StatusSamples.Where(s => s.At >= since).OrderBy(s => s.At).ToListAsync(ct);
            foreach (var g in rows.GroupBy(r => r.ServiceId))
                store.Seed(g.Key, g.Select(r => new StatusPoint(r.At, r.State, r.LatencyMs)));
        }
        catch (Exception ex) { log.LogWarning("Could not load status history: {Message}", ex.Message); }
    }

    private async Task Persist(IEnumerable<StatusSample> samples, CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            db.StatusSamples.AddRange(samples);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) { log.LogWarning("Could not save status samples: {Message}", ex.Message); }
    }

    private async Task Prune(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
        await db.StatusSamples.Where(s => s.At < cutoff).ExecuteDeleteAsync(ct);
        var auditCutoff = DateTimeOffset.UtcNow.AddDays(-180);
        await db.AuditEvents.Where(a => a.At < auditCutoff).ExecuteDeleteAsync(ct);
    }
}
