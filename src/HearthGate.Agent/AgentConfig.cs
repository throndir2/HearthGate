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
    // Extra game profiles (*.yaml). Default: the "game-profiles" folder next to agent.yaml. They override built-in profiles with the same id.
    public string? ProfilesDir { get; set; }

    [YamlIgnore] public string ConfigDirectory { get; set; } = AppContext.BaseDirectory;

    public static readonly IDeserializer Yaml = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties().Build();

    public static AgentConfig Load()
    {
        var path = Environment.GetEnvironmentVariable("HG_AGENT_CONFIG")
                   ?? Path.Combine(AppContext.BaseDirectory, "agent.yaml");
        if (!File.Exists(path)) return new AgentConfig();
        var config = Yaml.Deserialize<AgentConfig>(File.ReadAllText(path)) ?? new AgentConfig();
        config.ConfigDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        return config;
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
    // Server commands. A profile (built-in or from profilesDir) gives the channel, commands and stop steps;
    // values set here override it. Commands with the same id replace the profile command.
    public string? Profile { get; set; }
    public GameChannelConfig? Channel { get; set; }
    public List<GameCommandConfig> Commands { get; set; } = [];
    // Run before a stop or restart while the server runs. Null = use the profile; [] = none.
    public List<StopStepConfig>? StopSteps { get; set; }
}

public static class ChannelTypes
{
    public const string Rcon = "rcon";
    public const string Http = "http";
    public const string Script = "script";
}

// How the agent sends commands to the game server. Passwords come only from env vars or the game's own INI file.
public sealed class GameChannelConfig
{
    public string? Type { get; set; }
    // rcon: host:port (Source RCON protocol)
    public string? Address { get; set; }
    // http: base URL; auth none | basic | bearer
    public string? Url { get; set; }
    public string? Auth { get; set; }
    public string? Username { get; set; }
    // rcon and http
    public string? PasswordEnv { get; set; }
    // Optional: read the password from the game's settings file when the env var is empty, for example AdminPassword="...".
    public string? PasswordIni { get; set; }
    public string? PasswordIniKey { get; set; }
    // script: runs <script> -Server <scriptServer> -Action Command -Command "<rendered template>". Default: the game's script.
    public string? Script { get; set; }
    public string? ScriptServer { get; set; }
    public int? TimeoutSeconds { get; set; }
}

public sealed class GameCommandConfig
{
    public string Id { get; set; } = "";
    public string? Label { get; set; }
    public string? Description { get; set; }
    // control: games.control:<id> is enough. admin: needs games.admin:<id>.
    public string Level { get; set; } = "control";
    public bool Confirm { get; set; }
    public bool RequiresRunning { get; set; } = true;
    // Hidden commands are not in the UI; only stop steps can use them.
    public bool Hidden { get; set; }
    // Removes a profile command with the same id.
    public bool Disabled { get; set; }
    // rcon and script: the command text, for example "say {message}".
    public string? Template { get; set; }
    // http: method, path and an optional JSON body template, for example {"message":"{message}"}.
    public string Method { get; set; } = "POST";
    public string? Path { get; set; }
    public string? Body { get; set; }
    public List<GameCommandParamConfig> Params { get; set; } = [];
}

public static class ParamTypes
{
    public const string Text = "text";
    public const string Int = "int";
    public const string Choice = "choice";
}

public sealed class GameCommandParamConfig
{
    public string Name { get; set; } = "";
    public string? Label { get; set; }
    public string Type { get; set; } = ParamTypes.Text;
    public bool Required { get; set; } = true;
    public int? MaxLength { get; set; }
    public long? Min { get; set; }
    public long? Max { get; set; }
    public List<string>? Options { get; set; }
    public string? Default { get; set; }
    // Full-match regular expression for text values.
    public string? Pattern { get; set; }
}

// One stop step: run a command, wait a number of seconds, or wait until the server stops by itself.
public sealed class StopStepConfig
{
    public string? Command { get; set; }
    public Dictionary<string, string>? Params { get; set; }
    public int? Wait { get; set; }
    public int? WaitForStop { get; set; }
}

public sealed class GameProfile
{
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public GameChannelConfig? Channel { get; set; }
    public List<GameCommandConfig> Commands { get; set; } = [];
    public List<StopStepConfig>? StopSteps { get; set; }
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
