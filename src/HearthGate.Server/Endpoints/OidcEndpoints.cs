using System.Security.Claims;
using HearthGate.Server.Auth;
using HearthGate.Server.Config;
using HearthGate.Server.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace HearthGate.Server.Endpoints;

// Owns: optional OIDC (Authentik or any provider) sign-in and account linking.
// A link stores the provider's (issuer, subject) against a local user. Email is never used to merge accounts.
public static class OidcEndpoints
{
    public static void Map(WebApplication app)
    {
        var oidc = app.MapGroup("/api/auth/oidc").AddEndpointFilter(async (ctx, next) =>
        {
            var cfg = ctx.HttpContext.RequestServices.GetRequiredService<ConfigProvider>().Current.Auth.Oidc;
            var registered = ctx.HttpContext.RequestServices.GetRequiredService<OidcState>().Registered;
            return cfg is { Enabled: true } && registered ? await next(ctx) : Results.NotFound();
        });

        oidc.MapGet("/login", () => Results.Challenge(
            new AuthenticationProperties(new Dictionary<string, string?> { ["mode"] = "login" }) { RedirectUri = "/api/auth/oidc/complete" },
            [AuthSetup.OidcScheme])).RequireRateLimiting("auth");

        // POST with an antiforgery form field so another site cannot start a link for a signed-in user.
        oidc.MapPost("/link", (HttpContext ctx) =>
        {
            var u = ctx.Hg();
            if (!u.IsAuthenticated) return Results.Redirect("/signin");
            return Results.Challenge(
                new AuthenticationProperties(new Dictionary<string, string?> { ["mode"] = "link", ["uid"] = u.Id.ToString() }) { RedirectUri = "/api/auth/oidc/complete" },
                [AuthSetup.OidcScheme]);
        }).RequireRateLimiting("auth");

        oidc.MapGet("/complete", async (HttpContext ctx, AppDb db, ConfigProvider config, AccountService accounts, AuditService audit) =>
        {
            var result = await ctx.AuthenticateAsync(AuthSetup.ExternalScheme);
            await ctx.SignOutAsync(AuthSetup.ExternalScheme);
            if (!result.Succeeded || result.Principal is null) return Results.Redirect("/signin?error=oidc");
            var cfg = config.Current.Auth.Oidc!;
            var subClaim = result.Principal.FindFirst("sub");
            if (subClaim is null) return Results.Redirect("/signin?error=oidc");
            var issuer = subClaim.Issuer;
            var subject = subClaim.Value;
            var display = result.Principal.FindFirstValue("preferred_username") ?? result.Principal.FindFirstValue("name") ?? subject;
            var mode = result.Properties?.Items.TryGetValue("mode", out var m) == true ? m : "login";
            var existing = await db.ExternalLogins.Include(l => l.User).FirstOrDefaultAsync(l => l.Issuer == issuer && l.Subject == subject);

            if (mode == "link")
            {
                var uid = result.Properties!.Items.TryGetValue("uid", out var s) && int.TryParse(s, out var i) ? i : (int?)null;
                if (uid is null || uid != ctx.Hg().Id) return Results.Redirect("/profile?link=failed");
                if (existing is not null)
                    return Results.Redirect(existing.UserId == uid ? "/profile?link=already" : "/profile?link=taken");
                db.ExternalLogins.Add(new ExternalLogin { UserId = uid.Value, Provider = cfg.DisplayName, Issuer = issuer, Subject = subject, DisplayName = display });
                await db.SaveChangesAsync();
                await audit.Log("me.link", cfg.DisplayName, display);
                return Results.Redirect("/profile?link=ok");
            }

            User? user = existing?.User;
            if (user is null)
            {
                switch (cfg.AutoProvision)
                {
                    case "pending":
                        if (config.Current.Auth.AccountRequests && !await db.AccountRequests.AnyAsync(r => r.ExternalSubject == subject && r.ExternalIssuer == issuer && r.Status == RequestStatus.Pending))
                        {
                            db.AccountRequests.Add(new AccountRequest
                            {
                                Username = SafeUsername(display), Email = result.Principal.FindFirstValue("email"),
                                Message = $"Requested through {cfg.DisplayName}.", ExternalIssuer = issuer, ExternalSubject = subject,
                            });
                            await db.SaveChangesAsync();
                            await audit.Log("request.create", display, "oidc");
                        }
                        return Results.Redirect("/signin?notice=requested");
                    case "approved":
                        user = new User { Username = await UniqueUsername(db, SafeUsername(display)), Email = result.Principal.FindFirstValue("email") };
                        user.NormalizedUsername = AccountService.Normalize(user.Username);
                        user.ExternalLogins.Add(new ExternalLogin { Provider = cfg.DisplayName, Issuer = issuer, Subject = subject, DisplayName = display });
                        var members = await db.Groups.FirstOrDefaultAsync(g => g.Name == "Members");
                        if (members is not null) user.Groups.Add(new UserGroup { Group = members });
                        db.Users.Add(user);
                        await db.SaveChangesAsync();
                        await audit.Log("user.provision", user.Username, "oidc", actorOverride: user);
                        break;
                    default:
                        await audit.Log("auth.oidc_login", display, "not linked", false);
                        return Results.Redirect("/signin?error=not_linked");
                }
            }
            if (user.Disabled) return Results.Redirect("/signin?error=disabled");
            await ApplyGroupMap(db, user, result.Principal, cfg);
            user.LastLoginAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            await accounts.SignIn(user, viaExternal: true);
            await audit.Log("auth.oidc_login", user.Username, actorOverride: user);
            return Results.Redirect("/");
        });
    }

    private static async Task ApplyGroupMap(AppDb db, User user, ClaimsPrincipal principal, OidcConfig cfg)
    {
        if (cfg.GroupMap.Count == 0 || string.IsNullOrEmpty(cfg.GroupsClaim)) return;
        var claimed = principal.FindAll(cfg.GroupsClaim).Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var managed = cfg.GroupMap.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var wanted = cfg.GroupMap.Where(kv => claimed.Contains(kv.Key)).Select(kv => kv.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groups = await db.Groups.Where(g => managed.Contains(g.Name)).ToListAsync();
        await db.Entry(user).Collection(u => u.Groups).LoadAsync();
        foreach (var g in groups)
        {
            var has = user.Groups.Any(x => x.GroupId == g.Id);
            if (wanted.Contains(g.Name) && !has) user.Groups.Add(new UserGroup { GroupId = g.Id });
            if (!wanted.Contains(g.Name) && has) user.Groups.RemoveAll(x => x.GroupId == g.Id);
        }
    }

    private static string SafeUsername(string s)
    {
        var clean = new string(s.Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.').ToArray());
        if (clean.Length < 3) clean = "user" + Random.Shared.Next(1000, 9999);
        return clean.Length > 28 ? clean[..28] : clean;
    }

    private static async Task<string> UniqueUsername(AppDb db, string baseName)
    {
        var name = baseName;
        for (var i = 2; await db.Users.AnyAsync(u => u.NormalizedUsername == AccountService.Normalize(name)); i++) name = $"{baseName}{i}";
        return name;
    }
}

public sealed class OidcState(bool registered)
{
    public bool Registered { get; } = registered;
}
