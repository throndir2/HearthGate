namespace HearthGate.Contracts;

// Shared wire format between HearthGate.Server and HearthGate.Agent.

public sealed record HostSnapshot(
    string Os,
    double? CpuPercent,
    long? MemoryTotalBytes,
    long? MemoryUsedBytes,
    long UptimeSeconds,
    IReadOnlyList<DiskInfo> Disks,
    IReadOnlyList<ContainerInfo>? Containers,
    string? DockerError);

public sealed record DiskInfo(string Name, long TotalBytes, long FreeBytes);

public sealed record ContainerInfo(string Name, string Image, string State, string Status, string? Health);

public sealed record GameServerStatus(
    string Key,
    string State,
    int? Players,
    IReadOnlyList<string>? PlayerNames,
    string? Version,
    string? Message);

public static class GameStates
{
    public const string Running = "running";
    public const string Stopped = "stopped";
    public const string Starting = "starting";
    public const string Stopping = "stopping";
    public const string Unknown = "unknown";
}

public static class GameActions
{
    public const string Start = "start";
    public const string Stop = "stop";
    public const string Restart = "restart";
    public static readonly string[] All = [Start, Stop, Restart];
}

public sealed record GameActionResult(bool Ok, string Message);
