using HearthGate.Server.Auth;
using HearthGate.Server.Config;
using HearthGate.Server.Data;
using HearthGate.Server.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace HearthGate.Server.Endpoints;

// Owns: login, logout, account requests, set-password, and the signed-in user's own profile (password, TOTP, links).
public static class AccountEndpoints
{
    public sealed record LoginBody(string? Username, string? Password, string? Totp);
    public sealed record RequestBody(string? Username, string? Email, string? Message, string? Website, ProofOfWorkService.Challenge? Pow, string? Nonce);
    public sealed record SetPasswordBody(string? Token, string? Password);
    public sealed record ChangePasswordBody(string? Current, string? Password);
    public sealed record CodeBody(string? Code);

    private static IResult Error(string message, int status = 400) => Results.Json(new { error = message }, statusCode: status);

    public static void Map(WebApplication app)
    {
        var auth = app.MapGroup("/api/auth");

        auth.MapPost("/login", async (LoginBody body, AccountService accounts, AuditService audit) =>
        {
            var (outcome, user) = await accounts.Login(body.Username ?? "", body.Password ?? "", body.Totp);
            switch (outcome)
            {
                case LoginOutcome.LocalLoginDisabled: return Error("Password sign-in is turned off.", 403);
                case LoginOutcome.NeedsTotp: return Results.Ok(new { needsTotp = true });
                case LoginOutcome.Failed:
                    await audit.Log("auth.login", body.Username, success: false);
                    return Error("The username, password or code is not correct, or the account is locked for a short time.", 401);
            }
            await accounts.SignIn(user!);
            await audit.Log("auth.login", user!.Username, actorOverride: user);
            return Results.Ok(new { ok = true });
        }).RequireRateLimiting("auth");

        auth.MapPost("/logout", async (HttpContext ctx, AuditService audit) =>
        {
            if (ctx.Hg().IsAuthenticated) await audit.Log("auth.logout");
            await ctx.SignOutAsync(AuthSetup.CookieScheme);
            return Results.Ok(new { ok = true });
        });

        auth.MapPost("/set-password", async (SetPasswordBody body, AccountService accounts, AppDb db, AuditService audit) =>
        {
            if (AccountService.ValidatePassword(body.Password) is { } err) return Error(err);
            var user = await accounts.ConsumeSetupToken(body.Token ?? "");
            if (user is null) { await db.SaveChangesAsync(); return Error("This link is not valid or has expired. Ask the admin for a new link."); }
            accounts.SetPassword(user, body.Password!);
            user.FailedLogins = 0;
            user.LockoutUntil = null;
            await db.SaveChangesAsync();
            await audit.Log("auth.set_password", user.Username, actorOverride: user);
            return Results.Ok(new { ok = true });
        }).RequireRateLimiting("auth");

        app.MapPost("/api/account-requests", async (RequestBody body, ConfigProvider config, ProofOfWorkService pow, AppDb db, AuditService audit) =>
        {
            if (!config.Current.Auth.AccountRequests) return Error("Account requests are closed.", 403);
            if (!string.IsNullOrEmpty(body.Website)) return Results.Ok(new { ok = true }); // honeypot: pretend success
            if (!pow.Verify(body.Pow, body.Nonce)) return Error("The security check failed. Reload the page and try again.");
            if (AccountService.ValidateUsername(body.Username) is { } err) return Error(err);
            var email = body.Email?.Trim();
            if (email is { Length: > 0 } && (email.Length > 254 || !email.Contains('@'))) return Error("The email address is not valid.");
            var message = body.Message?.Trim();
            if (message is { Length: > 500 }) return Error("The message is too long (500 characters at most).");
            var norm = AccountService.Normalize(body.Username!);
            var pending = await db.AccountRequests.CountAsync(r => r.Status == RequestStatus.Pending);
            if (pending >= 200) return Error("Too many open requests. Try again later.", 429);
            // Same answer whether or not the name is taken, so the form cannot be used to find usernames.
            var taken = await db.Users.AnyAsync(u => u.NormalizedUsername == norm)
                || await db.AccountRequests.AnyAsync(r => r.Status == RequestStatus.Pending && r.Username.ToUpper() == norm);
            if (!taken)
            {
                db.AccountRequests.Add(new AccountRequest { Username = body.Username!.Trim(), Email = email, Message = message });
                await db.SaveChangesAsync();
                await audit.Log("request.create", body.Username!.Trim());
            }
            return Results.Ok(new { ok = true });
        }).RequireRateLimiting("request");

        var me = app.MapGroup("/api/me").AddEndpointFilter(async (ctx, next) =>
            ctx.HttpContext.Hg().IsAuthenticated ? await next(ctx) : Results.Unauthorized());

        me.MapPost("/password", async (ChangePasswordBody body, HttpContext ctx, AccountService accounts, AppDb db, AuditService audit) =>
        {
            var user = await db.Users.FirstAsync(u => u.Id == ctx.Hg().Id);
            if (user.PasswordHash is not null)
            {
                var (outcome, _) = await accounts.Login(user.Username, body.Current ?? "", null);
                if (outcome is LoginOutcome.Failed or LoginOutcome.LocalLoginDisabled) return Error("The current password is not correct.");
                await db.Entry(user).ReloadAsync();
            }
            if (AccountService.ValidatePassword(body.Password) is { } err) return Error(err);
            accounts.SetPassword(user, body.Password!);
            await db.SaveChangesAsync();
            await accounts.SignIn(user);
            await audit.Log("me.password");
            return Results.Ok(new { ok = true });
        }).RequireRateLimiting("auth");

        me.MapPost("/totp/setup", async (HttpContext ctx, AppDb db, ConfigProvider config) =>
        {
            var user = await db.Users.FirstAsync(u => u.Id == ctx.Hg().Id);
            if (user.TotpEnabled) return Error("Two-step sign-in is already on.");
            user.TotpSecret = AccountService.NewTotpSecret();
            await db.SaveChangesAsync();
            var issuer = Uri.EscapeDataString(config.Current.Site.Title);
            var uri = $"otpauth://totp/{issuer}:{Uri.EscapeDataString(user.Username)}?secret={user.TotpSecret}&issuer={issuer}&digits=6&period=30";
            return Results.Ok(new { secret = user.TotpSecret, uri });
        });

        me.MapPost("/totp/enable", async (CodeBody body, HttpContext ctx, AppDb db, AccountService accounts, AuditService audit) =>
        {
            var user = await db.Users.FirstAsync(u => u.Id == ctx.Hg().Id);
            if (user.TotpEnabled || user.TotpSecret is null) return Error("Start the setup first.");
            if (!AccountService.VerifyTotp(user, body.Code ?? "")) return Error("The code is not correct.");
            user.TotpEnabled = true;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync();
            await accounts.SignIn(user);
            await audit.Log("me.totp_enable");
            return Results.Ok(new { ok = true });
        }).RequireRateLimiting("auth");

        me.MapPost("/totp/disable", async (CodeBody body, HttpContext ctx, AppDb db, AccountService accounts, AuditService audit) =>
        {
            var user = await db.Users.FirstAsync(u => u.Id == ctx.Hg().Id);
            if (!user.TotpEnabled) return Error("Two-step sign-in is not on.");
            if (!AccountService.VerifyTotp(user, body.Code ?? "")) return Error("The code is not correct.");
            user.TotpEnabled = false;
            user.TotpSecret = null;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync();
            await accounts.SignIn(user);
            await audit.Log("me.totp_disable");
            return Results.Ok(new { ok = true });
        }).RequireRateLimiting("auth");

        me.MapGet("/links", async (HttpContext ctx, AppDb db) =>
            Results.Ok(await db.ExternalLogins.Where(l => l.UserId == ctx.Hg().Id)
                .Select(l => new { l.Id, l.Provider, l.DisplayName, l.LinkedAt }).ToListAsync()));

        me.MapDelete("/links/{id:int}", async (int id, HttpContext ctx, AppDb db, AuditService audit) =>
        {
            var uid = ctx.Hg().Id;
            var link = await db.ExternalLogins.FirstOrDefaultAsync(l => l.Id == id && l.UserId == uid);
            if (link is null) return Results.NotFound();
            var user = await db.Users.FirstAsync(u => u.Id == uid);
            var others = await db.ExternalLogins.CountAsync(l => l.UserId == uid && l.Id != id);
            if (user.PasswordHash is null && others == 0) return Error("Set a password first. Otherwise you cannot sign in after you unlink.");
            db.ExternalLogins.Remove(link);
            await db.SaveChangesAsync();
            await audit.Log("me.unlink", link.Provider);
            return Results.Ok(new { ok = true });
        });
    }
}
