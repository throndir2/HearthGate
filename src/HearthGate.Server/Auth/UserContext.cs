using System.Security.Claims;
using HearthGate.Server.Config;
using HearthGate.Server.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HearthGate.Server.Auth;

// Owns: the permission catalog and the per-request user context.
// Every endpoint asks UserContext for a permission; the UI only hides what the API already refuses.

public static class Permissions
{
    public const string StatusDetails = "status.details";
    public const string ServicesPrivate = "services.private";
    public const string GamesView = "games.view";
    public const string GamesControlAll = "games.control:*";
    public const string GamesAdminAll = "games.admin:*";
    public const string HostsView = "hosts.view";
    public static string GamesControl(string serverId) => $"games.control:{serverId}";
    // games.admin also gives games.control for the same server.
    public static string GamesAdmin(string serverId) => $"games.admin:{serverId}";

    public static readonly string[] Catalog = [StatusDetails, ServicesPrivate, GamesView, GamesControlAll, GamesAdminAll, HostsView];
}

public sealed class UserContext
{
    public static readonly UserContext Anonymous = new();

    public int? Id { get; init; }
    public string? Username { get; init; }
    public bool IsAdmin { get; init; }
    public bool TotpEnabled { get; init; }
    public bool HasPassword { get; init; }
    public bool AdminNeedsTotp { get; init; }
    public HashSet<string> Grants { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsAuthenticated => Id.HasValue;
    public bool IsAdminActive => IsAdmin && !AdminNeedsTotp;

    public bool Has(string permission) =>
        IsAdminActive
        || Grants.Contains(permission)
        || (permission.StartsWith("games.control:", StringComparison.OrdinalIgnoreCase)
            && (Grants.Contains(Permissions.GamesControlAll) || Grants.Contains(Permissions.GamesAdminAll)
                || Grants.Contains("games.admin:" + permission["games.control:".Length..])))
        || (permission.StartsWith("games.admin:", StringComparison.OrdinalIgnoreCase) && Grants.Contains(Permissions.GamesAdminAll));

    public bool CanSee(string visibility) => visibility switch
    {
        Visibility.Public => true,
        Visibility.Users => IsAuthenticated,
        Visibility.Admins => IsAdminActive,
        _ => false,
    };

    public IEnumerable<string> EffectivePermissions() =>
        IsAdminActive ? Permissions.Catalog.Append("admin") : Grants;
}

public static class UserContextExtensions
{
    public static UserContext Hg(this HttpContext ctx) =>
        ctx.Items.TryGetValue(UserContextMiddleware.ItemKey, out var v) && v is UserContext u ? u : UserContext.Anonymous;
}

// Loads the signed-in user on each request, checks the security stamp (session revocation) and disabled flag.
public sealed class UserContextMiddleware(RequestDelegate next)
{
    public const string ItemKey = "hg.user";
    public const string StampClaim = "hg:stamp";

    public async Task InvokeAsync(HttpContext ctx, AppDb db, IMemoryCache cache, ConfigProvider config)
    {
        var principal = ctx.User;
        if (principal.Identity?.IsAuthenticated == true
            && int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            var stamp = principal.FindFirstValue(StampClaim);
            var key = $"hg.user.{id}.{stamp}";
            if (!cache.TryGetValue(key, out UserContext? uc))
            {
                var user = await db.Users.AsNoTracking()
                    .Include(u => u.Groups).ThenInclude(g => g.Group)
                    .FirstOrDefaultAsync(u => u.Id == id);
                uc = user is null || user.Disabled || user.SecurityStamp != stamp ? null : Build(user, config.Current);
                cache.Set(key, uc, TimeSpan.FromSeconds(15));
            }
            if (uc is null)
            {
                await ctx.SignOutAsync(AuthSetup.CookieScheme);
                ctx.User = new ClaimsPrincipal(new ClaimsIdentity());
            }
            else ctx.Items[ItemKey] = uc;
        }
        await next(ctx);
    }

    private static UserContext Build(User user, HearthGateConfig cfg) => new()
    {
        Id = user.Id,
        Username = user.Username,
        IsAdmin = user.IsAdmin,
        TotpEnabled = user.TotpEnabled,
        HasPassword = user.PasswordHash is not null,
        AdminNeedsTotp = user.IsAdmin && cfg.Auth.RequireAdminTotp && !user.TotpEnabled,
        Grants = user.Groups.SelectMany(g => g.Group.PermissionList()).ToHashSet(StringComparer.OrdinalIgnoreCase),
    };
}
