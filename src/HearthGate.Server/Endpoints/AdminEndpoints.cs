using HearthGate.Server.Auth;
using HearthGate.Server.Config;
using HearthGate.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace HearthGate.Server.Endpoints;

// Owns: admin-only management (requests, users, groups, audit, config view). Requires an active admin (TOTP on).
public static class AdminEndpoints
{
    public sealed record UserUpdate(bool? IsAdmin, bool? Disabled, List<int>? GroupIds);
    public sealed record GroupBody(string? Name, List<string>? Permissions);

    private static IResult Error(string message, int status = 400) => Results.Json(new { error = message }, statusCode: status);

    public static void Map(WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").AddEndpointFilter(async (ctx, next) =>
        {
            var u = ctx.HttpContext.Hg();
            if (!u.IsAuthenticated) return Results.Unauthorized();
            if (u.AdminNeedsTotp) return Error("Turn on two-step sign-in to use admin tools.", 403);
            return u.IsAdminActive ? await next(ctx) : Results.Forbid();
        });

        admin.MapGet("/requests", async (AppDb db) =>
            Results.Ok(await db.AccountRequests.OrderByDescending(r => r.Id).Take(200)
                .Select(r => new { r.Id, r.Username, r.Email, r.Message, r.Status, r.CreatedAt, r.DecidedAt, External = r.ExternalSubject != null })
                .ToListAsync()));

        admin.MapPost("/requests/{id:int}/approve", async (int id, AppDb db, AccountService accounts, AuditService audit, ConfigProvider config) =>
        {
            var req = await db.AccountRequests.FirstOrDefaultAsync(r => r.Id == id && r.Status == RequestStatus.Pending);
            if (req is null) return Results.NotFound();
            var norm = AccountService.Normalize(req.Username);
            if (await db.Users.AnyAsync(u => u.NormalizedUsername == norm)) return Error("A user with this name already exists.");
            var user = new User { Username = req.Username, NormalizedUsername = norm, Email = req.Email };
            db.Users.Add(user);
            var defaultGroup = await db.Groups.FirstOrDefaultAsync(g => g.Name == "Members");
            if (defaultGroup is not null) user.Groups.Add(new UserGroup { Group = defaultGroup });
            if (req.ExternalSubject is not null)
                user.ExternalLogins.Add(new ExternalLogin { Provider = config.Current.Auth.Oidc?.DisplayName ?? "OIDC", Issuer = req.ExternalIssuer!, Subject = req.ExternalSubject, DisplayName = req.Username });
            req.Status = RequestStatus.Approved;
            req.DecidedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            var token = await accounts.CreateSetupToken(user);
            await audit.Log("request.approve", req.Username);
            return Results.Ok(new { ok = true, setPasswordPath = $"/set-password?token={token}" });
        });

        admin.MapPost("/requests/{id:int}/reject", async (int id, AppDb db, AuditService audit) =>
        {
            var req = await db.AccountRequests.FirstOrDefaultAsync(r => r.Id == id && r.Status == RequestStatus.Pending);
            if (req is null) return Results.NotFound();
            req.Status = RequestStatus.Rejected;
            req.DecidedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            await audit.Log("request.reject", req.Username);
            return Results.Ok(new { ok = true });
        });

        admin.MapGet("/users", async (AppDb db) =>
            Results.Ok(await db.Users.OrderBy(u => u.Username).Select(u => new
            {
                u.Id, u.Username, u.Email, u.IsAdmin, u.Disabled, u.TotpEnabled, u.CreatedAt, u.LastLoginAt,
                HasPassword = u.PasswordHash != null,
                Locked = u.LockoutUntil > DateTimeOffset.UtcNow,
                GroupIds = u.Groups.Select(g => g.GroupId).ToList(),
                Links = u.ExternalLogins.Select(l => l.Provider).ToList(),
            }).ToListAsync()));

        admin.MapPut("/users/{id:int}", async (int id, UserUpdate body, HttpContext ctx, AppDb db, AuditService audit) =>
        {
            var user = await db.Users.Include(u => u.Groups).FirstOrDefaultAsync(u => u.Id == id);
            if (user is null) return Results.NotFound();
            var self = ctx.Hg().Id == id;
            if (self && (body.IsAdmin == false || body.Disabled == true)) return Error("You cannot remove your own admin access or disable yourself.");
            var changes = new List<string>();
            if (body.IsAdmin is { } a && a != user.IsAdmin) { user.IsAdmin = a; changes.Add($"admin={a}"); }
            if (body.Disabled is { } d && d != user.Disabled) { user.Disabled = d; changes.Add($"disabled={d}"); if (d) user.SecurityStamp = Guid.NewGuid().ToString("N"); }
            if (body.GroupIds is { } ids)
            {
                var valid = await db.Groups.Where(g => ids.Contains(g.Id)).Select(g => g.Id).ToListAsync();
                user.Groups.RemoveAll(g => !valid.Contains(g.GroupId));
                foreach (var gid in valid.Where(g => user.Groups.All(x => x.GroupId != g))) user.Groups.Add(new UserGroup { GroupId = gid });
                changes.Add($"groups={string.Join(',', valid)}");
            }
            await db.SaveChangesAsync();
            await audit.Log("user.update", user.Username, string.Join("; ", changes));
            return Results.Ok(new { ok = true });
        });

        admin.MapPost("/users/{id:int}/reset-link", async (int id, AppDb db, AccountService accounts, AuditService audit) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user is null) return Results.NotFound();
            var token = await accounts.CreateSetupToken(user);
            await audit.Log("user.reset_link", user.Username);
            return Results.Ok(new { setPasswordPath = $"/set-password?token={token}" });
        });

        admin.MapPost("/users/{id:int}/revoke-sessions", async (int id, AppDb db, AuditService audit) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user is null) return Results.NotFound();
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync();
            await audit.Log("user.revoke_sessions", user.Username);
            return Results.Ok(new { ok = true });
        });

        admin.MapPost("/users/{id:int}/reset-totp", async (int id, HttpContext ctx, AppDb db, AuditService audit) =>
        {
            if (ctx.Hg().Id == id) return Error("Use your profile page to change your own two-step sign-in.");
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user is null) return Results.NotFound();
            user.TotpEnabled = false;
            user.TotpSecret = null;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync();
            await audit.Log("user.reset_totp", user.Username);
            return Results.Ok(new { ok = true });
        });

        admin.MapGet("/groups", async (AppDb db) =>
            Results.Ok(new
            {
                Catalog = Permissions.Catalog,
                Groups = await db.Groups.OrderBy(g => g.Name).Select(g => new { g.Id, g.Name, g.Permissions, Members = g.Users.Count }).ToListAsync(),
            }));

        admin.MapPost("/groups", async (GroupBody body, AppDb db, AuditService audit) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 40) return Error("Enter a group name (40 characters at most).");
            if (await db.Groups.AnyAsync(g => g.Name == body.Name.Trim())) return Error("A group with this name exists.");
            var g = new Group { Name = body.Name.Trim(), Permissions = CleanPermissions(body.Permissions) };
            db.Groups.Add(g);
            await db.SaveChangesAsync();
            await audit.Log("group.create", g.Name, g.Permissions);
            return Results.Ok(new { g.Id });
        });

        admin.MapPut("/groups/{id:int}", async (int id, GroupBody body, AppDb db, AuditService audit) =>
        {
            var g = await db.Groups.FirstOrDefaultAsync(x => x.Id == id);
            if (g is null) return Results.NotFound();
            if (!string.IsNullOrWhiteSpace(body.Name)) g.Name = body.Name.Trim()[..Math.Min(40, body.Name.Trim().Length)];
            if (body.Permissions is not null) g.Permissions = CleanPermissions(body.Permissions);
            await db.SaveChangesAsync();
            await audit.Log("group.update", g.Name, g.Permissions);
            return Results.Ok(new { ok = true });
        });

        admin.MapDelete("/groups/{id:int}", async (int id, AppDb db, AuditService audit) =>
        {
            var g = await db.Groups.FirstOrDefaultAsync(x => x.Id == id);
            if (g is null) return Results.NotFound();
            db.Groups.Remove(g);
            await db.SaveChangesAsync();
            await audit.Log("group.delete", g.Name);
            return Results.Ok(new { ok = true });
        });

        admin.MapGet("/audit", async (int? before, AppDb db) =>
        {
            var q = db.AuditEvents.AsQueryable();
            if (before is { } b) q = q.Where(a => a.Id < b);
            return Results.Ok(await q.OrderByDescending(a => a.Id).Take(100).ToListAsync());
        });

        admin.MapGet("/config", (ConfigProvider config) =>
        {
            var c = config.Current;
            return Results.Ok(new
            {
                Services = c.Services.Select(s => new { s.Id, s.Name, s.Visibility, Check = s.Check?.Type ?? (s.Url is null ? "none" : "http"), Target = s.Check?.Target ?? s.Url }),
                Hosts = c.Hosts.Select(h => new { h.Id, h.Name, h.AgentUrl, TokenSet = !string.IsNullOrEmpty(ConfigProvider.Secret(h.TokenEnv)) }),
                GameServers = c.GameServers.Select(g => new { g.Id, g.Name, g.Host, g.AgentKey }),
                GitHubOwner = c.GitHub.Owner,
                Oidc = c.Auth.Oidc is { Enabled: true } o ? new { o.Authority, o.ClientId, o.AutoProvision } : null,
            });
        });
    }

    private static string CleanPermissions(IEnumerable<string>? perms) =>
        string.Join(' ', (perms ?? []).Select(p => p.Trim())
            .Where(p => Permissions.Catalog.Contains(p) || System.Text.RegularExpressions.Regex.IsMatch(p, "^games\\.(control|admin):[A-Za-z0-9_-]{1,64}$"))
            .Distinct());
}
