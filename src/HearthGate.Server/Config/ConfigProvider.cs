using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace HearthGate.Server.Config;

// Owns: loading, validating and hot-reloading hearthgate.yaml. Readers call Current.
public sealed class ConfigProvider : IDisposable
{
    private readonly ILogger<ConfigProvider> _log;
    private readonly string _path;
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;

    public HearthGateConfig Current { get; private set; }
    public event Action? Changed;

    public ConfigProvider(ILogger<ConfigProvider> log, IHostEnvironment env)
    {
        _log = log;
        _path = Environment.GetEnvironmentVariable("HG_CONFIG") ?? "/data/hearthgate.yaml";
        if (!File.Exists(_path))
        {
            _log.LogWarning("Config file {Path} not found. Using the example config.", _path);
            _path = System.IO.Path.Combine(AppContext.BaseDirectory, "hearthgate.example.yaml");
        }
        Current = Load(_path);
        Watch();
    }

    public static HearthGateConfig Load(string path)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        var cfg = deserializer.Deserialize<HearthGateConfig>(File.ReadAllText(path)) ?? new HearthGateConfig();
        var errors = Validate(cfg);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
        return cfg;
    }

    public static List<string> Validate(HearthGateConfig cfg)
    {
        var errors = new List<string>();
        void Unique(IEnumerable<string> ids, string kind)
        {
            foreach (var g in ids.GroupBy(i => i, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                errors.Add($"Duplicate {kind} id '{g.Key}'.");
        }
        Unique(cfg.Services.Select(s => s.Id), "service");
        Unique(cfg.Hosts.Select(s => s.Id), "host");
        Unique(cfg.GameServers.Select(s => s.Id), "game server");
        foreach (var s in cfg.Services)
        {
            if (string.IsNullOrWhiteSpace(s.Id) || string.IsNullOrWhiteSpace(s.Name)) errors.Add("Each service needs an id and a name.");
            if (s.Url is not null && !IsHttpUrl(s.Url)) errors.Add($"Service '{s.Id}' url must be http(s).");
            if (s.Visibility is not (Visibility.Public or Visibility.Users or Visibility.Admins)) errors.Add($"Service '{s.Id}' has unknown visibility '{s.Visibility}'.");
            if (s.Check is { Type: not ("http" or "tcp" or "none") }) errors.Add($"Service '{s.Id}' has unknown check type.");
        }
        foreach (var g in cfg.GameServers)
            if (!cfg.Hosts.Any(h => h.Id == g.Host)) errors.Add($"Game server '{g.Id}' uses unknown host '{g.Host}'.");
        return errors;
    }

    private static bool IsHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https");

    private void Watch()
    {
        var full = System.IO.Path.GetFullPath(_path);
        _debounce = new Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(System.IO.Path.GetDirectoryName(full)!, System.IO.Path.GetFileName(full))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += (_, _) => _debounce.Change(500, Timeout.Infinite);
        _watcher.Created += (_, _) => _debounce.Change(500, Timeout.Infinite);
        _watcher.Renamed += (_, _) => _debounce.Change(500, Timeout.Infinite);
    }

    private void Reload()
    {
        try
        {
            Current = Load(_path);
            _log.LogInformation("Config reloaded.");
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            _log.LogError("Config reload failed; keeping the previous config. {Message}", ex.Message);
        }
    }

    public static string? Secret(string? envName) =>
        string.IsNullOrWhiteSpace(envName) ? null : Environment.GetEnvironmentVariable(envName);

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
    }
}
