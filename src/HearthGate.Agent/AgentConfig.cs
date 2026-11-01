using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace HearthGate.Agent;

// Owns: agent.yaml. Game servers listed here are the ONLY things the agent can start or stop.

public sealed class AgentConfig
{
    public string Listen { get; set; } = "http://0.0.0.0:7177";
    public string TokenEnv { get; set; } = "HG_AGENT_TOKEN";
    public List<string> AllowedClients { get; set; } = [];
    public DockerConfig? Docker { get; set; }
    public List<string> Disks { get; set; } = [];
    public List<GameConfig> Games { get; set; } = [];

    public static AgentConfig Load()
    {
        var path = Environment.GetEnvironmentVariable("HG_AGENT_CONFIG")
                   ?? Path.Combine(AppContext.BaseDirectory, "agent.yaml");
        if (!File.Exists(path)) return new AgentConfig();
        return new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties().Build().Deserialize<AgentConfig>(File.ReadAllText(path)) ?? new AgentConfig();
    }
}

public sealed class DockerConfig
{
    // Use a read-only docker socket proxy URL (for example http://docker-proxy:2375). Never the raw socket.
    public string Endpoint { get; set; } = "";
}

public static class GameKinds
{
    public const string Script = "script";
    public const string Process = "process";
    public const string WindowsService = "windowsService";
    public const string Docker = "docker";
}

public sealed class GameConfig
{
    public string Key { get; set; } = "";
    public string Kind { get; set; } = GameKinds.Process;
    // script: a control script with "-Server <scriptServer> -Action Status|Start|Stop|Restart [-AsJson]"
    public string? Script { get; set; }
    public string? ScriptServer { get; set; }
    // process
    public string? Exe { get; set; }
    public string? Args { get; set; }
    public string? WorkingDir { get; set; }
    public List<string> ProcessNames { get; set; } = [];
    // windowsService
    public string? Service { get; set; }
    // docker (needs a socket proxy with POST=1 and CONTAINERS=1)
    public string? Container { get; set; }
    public int StopTimeoutSeconds { get; set; } = 60;
    public PalworldRestConfig? PalworldRest { get; set; }
}

public sealed class PalworldRestConfig
{
    public string Url { get; set; } = "http://127.0.0.1:8212";
    public string PasswordEnv { get; set; } = "HG_PALWORLD_ADMIN_PASSWORD";
    // Optional: read AdminPassword from PalWorldSettings.ini when the env var is empty (no copy of the secret).
    public string? SettingsIni { get; set; }
    public int ShutdownWaitSeconds { get; set; } = 30;
    public string ShutdownMessage { get; set; } = "The server will restart soon. Please log out.";
}
