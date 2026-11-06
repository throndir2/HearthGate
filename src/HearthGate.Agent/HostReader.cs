using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using HearthGate.Contracts;

namespace HearthGate.Agent;

// Owns: reading host metrics (CPU, memory, disks, uptime) and container state. Read-only.
public sealed class HostReader(AgentConfig config, IHttpClientFactory httpFactory, ContainerStatsReader statsReader)
{
    public async Task<HostSnapshot> Read(CancellationToken ct)
    {
        var cpu = await CpuPercent(ct);
        var (total, used) = Memory();
        var disks = Disks();
        List<ContainerInfo>? containers = null;
        string? dockerError = null;
        if (config.Docker is { Endpoint.Length: > 0 })
        {
            try { containers = await Containers(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { dockerError = "Docker is not reachable."; }
        }
        return new HostSnapshot(RuntimeInformation.OSDescription, cpu, total, used, Environment.TickCount64 / 1000, disks, containers, dockerError);
    }

    private List<DiskInfo> Disks()
    {
        var wanted = config.Disks;
        return DriveInfo.GetDrives()
            .Where(d => wanted.Count > 0
                ? wanted.Contains(d.Name, StringComparer.OrdinalIgnoreCase) || wanted.Contains(d.Name.TrimEnd('/', '\\'), StringComparer.OrdinalIgnoreCase)
                : d.DriveType == DriveType.Fixed && (!OperatingSystem.IsLinux() || d.Name == "/"))
            .Where(d => d.IsReady && d.TotalSize > 0)
            .Select(d => new DiskInfo(d.Name, d.TotalSize, d.AvailableFreeSpace))
            .ToList();
    }

    private async Task<List<ContainerInfo>> Containers(CancellationToken ct)
    {
        var client = httpFactory.CreateClient("docker");
        var list = await client.GetFromJsonAsync<List<DockerContainer>>(DockerHttp.BaseUrl(config.Docker!.Endpoint) + "/containers/json?all=true", ct) ?? [];
        var stats = await statsReader.Read(list.Where(c => c.State == "running").Select(c => c.Id).ToList(), ct);
        return list.Select(c =>
        {
            var name = c.Names?.FirstOrDefault()?.TrimStart('/') ?? c.Id[..12];
            string? health = c.Status?.Contains("(healthy)") == true ? "healthy"
                : c.Status?.Contains("(unhealthy)") == true ? "unhealthy"
                : c.Status?.Contains("(health: starting)") == true ? "starting" : null;
            // Published ports only (host side), for example "8080->80/tcp". IPv4 and IPv6 entries are merged.
            var ports = (c.Ports ?? []).Where(p => p.PublicPort is > 0)
                .Select(p => p.PublicPort == p.PrivatePort ? $"{p.PublicPort}/{p.Type}" : $"{p.PublicPort}->{p.PrivatePort}/{p.Type}")
                .Distinct().OrderBy(p => int.Parse(p.Split('-', '/')[0])).ToList();
            var project = c.Labels?.GetValueOrDefault("com.docker.compose.project");
            return new ContainerInfo(name, c.Image ?? "", c.State ?? "unknown", c.Status ?? "", health, ports, project, stats.GetValueOrDefault(c.Id));
        }).OrderBy(c => c.Name).ToList();
    }

    private sealed class DockerContainer
    {
        public string Id { get; set; } = "";
        public List<string>? Names { get; set; }
        public string? Image { get; set; }
        public string? State { get; set; }
        public string? Status { get; set; }
        public List<DockerPort>? Ports { get; set; }
        public Dictionary<string, string>? Labels { get; set; }
    }

    private sealed class DockerPort
    {
        public int PrivatePort { get; set; }
        public int? PublicPort { get; set; }
        public string Type { get; set; } = "tcp";
    }

    // ---- CPU ----
    private static async Task<double?> CpuPercent(CancellationToken ct)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var a = ReadProcStat();
                await Task.Delay(500, ct);
                var b = ReadProcStat();
                var total = b.Total - a.Total;
                return total <= 0 ? null : Math.Round(100.0 * (total - (b.Idle - a.Idle)) / total, 1);
            }
            if (OperatingSystem.IsWindows())
            {
                GetSystemTimes(out var i1, out var k1, out var u1);
                await Task.Delay(500, ct);
                GetSystemTimes(out var i2, out var k2, out var u2);
                var idle = i2 - i1;
                var total = (k2 - k1) + (u2 - u1); // kernel time includes idle time
                return total <= 0 ? null : Math.Round(100.0 * (total - idle) / total, 1);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { }
        return null;
    }

    private static (long Total, long Idle) ReadProcStat()
    {
        var parts = File.ReadLines("/proc/stat").First().Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(long.Parse).ToArray();
        return (parts.Sum(), parts[3] + (parts.Length > 4 ? parts[4] : 0));
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    // ---- Memory ----
    private static (long?, long?) Memory()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var info = File.ReadAllLines("/proc/meminfo")
                    .Select(l => l.Split(':', 2))
                    .ToDictionary(p => p[0], p => long.Parse(p[1].Trim().Split(' ')[0]) * 1024);
                var total = info["MemTotal"];
                return (total, total - info.GetValueOrDefault("MemAvailable", info["MemFree"]));
            }
            if (OperatingSystem.IsWindows())
            {
                var m = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
                if (GlobalMemoryStatusEx(ref m)) return ((long)m.TotalPhys, (long)(m.TotalPhys - m.AvailPhys));
            }
        }
        catch { }
        var gc = GC.GetGCMemoryInfo();
        return (gc.TotalAvailableMemoryBytes, null);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
