using HearthGate.Server.Auth;
using HearthGate.Server.Config;
using HearthGate.Server.GitHub;
using HearthGate.Server.Security;
using HearthGate.Server.Status;
using Microsoft.AspNetCore.Antiforgery;

namespace HearthGate.Server.Endpoints;

// Owns: anonymous-safe endpoints. Guest responses carry only names, public links and a coarse status.
public static class PublicEndpoints
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/site", (ConfigProvider config) =>
        {
            var c = config.Current;
            return Results.Ok(new
            {
                c.Site.Title, c.Site.Tagline, c.Site.AccentColor, c.Site.Logo,
                FooterLinks = c.Site.FooterLinks.Select(l => new { l.Label, l.Url }),
                Categories = c.Categories.Select(x => new { x.Id, x.Name, x.Icon }),
                Auth = new
                {
                    c.Auth.LocalLogin,
                    c.Auth.AccountRequests,
                    Oidc = c.Auth.Oidc is { Enabled: true } o ? new { o.DisplayName } : null,
                },
                ShowProjects = !string.IsNullOrWhiteSpace(c.GitHub.Owner),
                ShowGames = c.GameServers.Count > 0,
            });
        });

        api.MapGet("/session", (HttpContext ctx, IAntiforgery af) =>
        {
            var tokens = af.GetAndStoreTokens(ctx);
            var u = ctx.Hg();
            return Results.Ok(new
            {
                Csrf = tokens.RequestToken,
                User = u.IsAuthenticated ? new
                {
                    u.Id, u.Username, u.IsAdmin, u.AdminNeedsTotp, u.TotpEnabled, u.HasPassword,
                    Permissions = u.EffectivePermissions(),
                } : null,
            });
        });

        api.MapGet("/services", (HttpContext ctx, ConfigProvider config, StatusStore store) =>
        {
            var u = ctx.Hg();
            var details = u.Has(Permissions.StatusDetails);
            var visible = config.Current.Services.Where(s => u.CanSee(s.Visibility)).ToList();
            var items = visible.Select(s =>
            {
                var monitored = s.Check is { Type: not "none" } || (s.Check is null && s.Url is not null);
                var st = store.Get(s.Id);
                lock (st)
                {
                    return new ServiceDto
                    {
                        Id = s.Id, Name = s.Name, Description = s.Description, Icon = s.Icon, Category = s.Category, Url = s.Url,
                        Status = monitored ? st.State : null,
                        LastChecked = monitored ? st.LastChecked : null,
                        Details = details && monitored ? new ServiceDetailsDto
                        {
                            LatencyMs = st.LatencyMs,
                            LastError = st.LastError,
                            LastChange = st.LastChange,
                            Uptime24h = store.Uptime(s.Id, TimeSpan.FromHours(24)),
                            History = st.History.TakeLast(60).Select(p => new { p.At, p.State, p.LatencyMs }).ToList<object>(),
                        } : null,
                    };
                }
            }).ToList();
            var states = items.Where(i => i.Status is not null).Select(i => i.Status!).ToList();
            var overall = states.Count == 0 || states.All(s => s == ServiceState.Unknown) ? ServiceState.Unknown
                : states.Any(s => s == ServiceState.Down) ? ServiceState.Down
                : states.Any(s => s == ServiceState.Degraded) ? ServiceState.Degraded
                : ServiceState.Up;
            return Results.Ok(new { Overall = overall, Services = items });
        });

        api.MapGet("/projects", async (GitHubShowcaseService gh, CancellationToken ct) => Results.Ok(await gh.GetProjects(ct)));

        api.MapGet("/pow", (ProofOfWorkService pow) => Results.Ok(pow.Create())).RequireRateLimiting("pow");
    }

    public sealed class ServiceDto
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string? Description { get; init; }
        public string? Icon { get; init; }
        public string? Category { get; init; }
        public string? Url { get; init; }
        public string? Status { get; init; }
        public DateTimeOffset? LastChecked { get; init; }
        public ServiceDetailsDto? Details { get; init; }
    }

    public sealed class ServiceDetailsDto
    {
        public int? LatencyMs { get; init; }
        public string? LastError { get; init; }
        public DateTimeOffset? LastChange { get; init; }
        public double? Uptime24h { get; init; }
        public List<object> History { get; init; } = [];
    }
}
