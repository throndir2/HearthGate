using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using HearthGate.Server.Config;

namespace HearthGate.Server.GitHub;

// Owns: the cached list of public GitHub projects. Fetches server-side only; tokens never reach the browser.

public sealed record ProjectDto(
    string Name, string? Description, string? Language, int Stars, int Forks,
    DateTimeOffset UpdatedAt, IReadOnlyList<string> Topics, string Url);

public sealed class GitHubShowcaseService(IHttpClientFactory httpFactory, ConfigProvider config, ILogger<GitHubShowcaseService> log)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<RepoJson> _repos = [];
    private DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;
    private string? _etag;

    public async Task<IReadOnlyList<ProjectDto>> GetProjects(CancellationToken ct)
    {
        var cfg = config.Current.GitHub;
        if (string.IsNullOrWhiteSpace(cfg.Owner)) return [];
        if (DateTimeOffset.UtcNow - _fetchedAt > TimeSpan.FromMinutes(Math.Max(5, cfg.CacheMinutes)))
            await Refresh(cfg, ct);
        return Filter(_repos, cfg);
    }

    private async Task Refresh(GitHubConfig cfg, CancellationToken ct)
    {
        // With an empty cache, wait for the running fetch; otherwise serve the cache while another request refreshes.
        if (_repos.Count == 0) await _lock.WaitAsync(ct);
        else if (!await _lock.WaitAsync(0, ct)) return;
        try
        {
            if (DateTimeOffset.UtcNow - _fetchedAt <= TimeSpan.FromMinutes(Math.Max(5, cfg.CacheMinutes))) return;
            var client = httpFactory.CreateClient("github");
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/users/{Uri.EscapeDataString(cfg.Owner!)}/repos?per_page=100&type=owner&sort=updated");
            var token = ConfigProvider.Secret(cfg.TokenEnv);
            if (!string.IsNullOrEmpty(token)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (_etag is not null) req.Headers.TryAddWithoutValidation("If-None-Match", _etag);
            using var resp = await client.SendAsync(req, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotModified) { _fetchedAt = DateTimeOffset.UtcNow; return; }
            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning("GitHub fetch failed with {Status}.", (int)resp.StatusCode);
                _fetchedAt = DateTimeOffset.UtcNow.AddMinutes(-Math.Max(5, cfg.CacheMinutes) + 5); // retry in 5 min
                return;
            }
            _repos = await resp.Content.ReadFromJsonAsync<List<RepoJson>>(ct) ?? [];
            _etag = resp.Headers.ETag?.Tag;
            _fetchedAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning("GitHub fetch failed: {Message}", ex.Message);
            _fetchedAt = DateTimeOffset.UtcNow.AddMinutes(-Math.Max(5, cfg.CacheMinutes) + 5);
        }
        finally { _lock.Release(); }
    }

    private static List<ProjectDto> Filter(IEnumerable<RepoJson> repos, GitHubConfig cfg)
    {
        var include = cfg.Include.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var exclude = cfg.Exclude.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return repos
            .Where(r => !r.Private)
            .Where(r => include.Count == 0 || include.Contains(r.Name))
            .Where(r => !exclude.Contains(r.Name))
            .Where(r => cfg.IncludeForks || !r.Fork)
            .Where(r => cfg.IncludeArchived || !r.Archived)
            .Where(r => !cfg.HostedSites.ContainsKey(r.Name))
            .Where(r => !cfg.HideReposWithHomepage || string.IsNullOrWhiteSpace(r.Homepage))
            .OrderByDescending(r => r.PushedAt ?? r.UpdatedAt)
            .Select(r => new ProjectDto(r.Name, r.Description, r.Language, r.Stars, r.Forks,
                r.PushedAt ?? r.UpdatedAt, r.Topics ?? [], r.HtmlUrl))
            .ToList();
    }

    private sealed class RepoJson
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("language")] public string? Language { get; set; }
        [JsonPropertyName("stargazers_count")] public int Stars { get; set; }
        [JsonPropertyName("forks_count")] public int Forks { get; set; }
        [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
        [JsonPropertyName("pushed_at")] public DateTimeOffset? PushedAt { get; set; }
        [JsonPropertyName("topics")] public List<string>? Topics { get; set; }
        [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
        [JsonPropertyName("homepage")] public string? Homepage { get; set; }
        [JsonPropertyName("fork")] public bool Fork { get; set; }
        [JsonPropertyName("archived")] public bool Archived { get; set; }
        [JsonPropertyName("private")] public bool Private { get; set; }
    }
}
