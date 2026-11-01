namespace HearthGate.Server.Config;

// Owns: the shape of hearthgate.yaml. Secrets are never stored here, only env var names (*Env).

public sealed class HearthGateConfig
{
    public SiteConfig Site { get; set; } = new();
    public List<CategoryConfig> Categories { get; set; } = [];
    public List<ServiceConfig> Services { get; set; } = [];
    public List<HostConfig> Hosts { get; set; } = [];
    public List<GameServerConfig> GameServers { get; set; } = [];
    [YamlDotNet.Serialization.YamlMember(Alias = "github")]
    public GitHubConfig GitHub { get; set; } = new();
    public AuthConfig Auth { get; set; } = new();
    public SecurityConfig Security { get; set; } = new();
}

public sealed class SiteConfig
{
    public string Title { get; set; } = "HearthGate";
    public string Tagline { get; set; } = "Everything I host, in one warm place.";
    public string AccentColor { get; set; } = "#f97316";
    public string? Logo { get; set; }
    public string? PublicUrl { get; set; }
    public List<LinkConfig> FooterLinks { get; set; } = [];
}

public sealed class LinkConfig
{
    public string Label { get; set; } = "";
    public string Url { get; set; } = "";
}

public sealed class CategoryConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Icon { get; set; }
}

public static class Visibility
{
    public const string Public = "public";
    public const string Users = "users";
    public const string Admins = "admins";
}

public sealed class ServiceConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public string? Category { get; set; }
    public string? Url { get; set; }
    public string Visibility { get; set; } = Config.Visibility.Public;
    public CheckConfig? Check { get; set; }
}

public sealed class CheckConfig
{
    public string Type { get; set; } = "http"; // http | tcp | none
    public string? Target { get; set; } // internal URL or host:port; defaults to service url
    public int IntervalSeconds { get; set; } = 60;
    public int TimeoutSeconds { get; set; } = 8;
    public int SlowMs { get; set; } = 3000;
    public int FailuresBeforeDown { get; set; } = 2;
    public List<int>? ExpectedStatus { get; set; } // default: 200-399
    public bool InsecureTls { get; set; }
}

public sealed class HostConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string AgentUrl { get; set; } = "";
    public string TokenEnv { get; set; } = "";
}

public sealed class GameServerConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public string AgentKey { get; set; } = "";
    public string? Game { get; set; }
    public string? Icon { get; set; }
    public string? ConnectInfo { get; set; }
    public bool ConnectInfoPublic { get; set; } // show the connect address to guests too
    public string Visibility { get; set; } = Config.Visibility.Public;
}

public sealed class GitHubConfig
{
    public string? Owner { get; set; }
    public string? TokenEnv { get; set; }
    public int CacheMinutes { get; set; } = 60;
    public bool IncludeForks { get; set; }
    public bool IncludeArchived { get; set; }
    public bool HideReposWithHomepage { get; set; }
    public List<string> Include { get; set; } = [];
    public List<string> Exclude { get; set; } = [];
    public Dictionary<string, string> HostedSites { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AuthConfig
{
    public bool LocalLogin { get; set; } = true;
    public bool AccountRequests { get; set; } = true;
    public bool RequireAdminTotp { get; set; } = true;
    public int SessionHours { get; set; } = 24 * 7;
    public List<GroupSeedConfig> Groups { get; set; } = [];
    public OidcConfig? Oidc { get; set; }
}

public sealed class GroupSeedConfig
{
    public string Name { get; set; } = "";
    public List<string> Permissions { get; set; } = [];
}

public sealed class OidcConfig
{
    public bool Enabled { get; set; }
    public string DisplayName { get; set; } = "Authentik";
    public string Authority { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecretEnv { get; set; } = "HG_OIDC_CLIENT_SECRET";
    public string AutoProvision { get; set; } = "none"; // none | pending | approved
    public string? GroupsClaim { get; set; } = "groups";
    public Dictionary<string, string> GroupMap { get; set; } = [];
}

public sealed class SecurityConfig
{
    public List<string> TrustedProxies { get; set; } = [];
    public int ProofOfWorkBits { get; set; } = 16;
}
