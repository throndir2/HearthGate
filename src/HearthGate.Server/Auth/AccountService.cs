using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HearthGate.Server.Config;
using HearthGate.Server.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OtpNet;

namespace HearthGate.Server.Auth;

// Owns: password hashing, login with lockout and TOTP, set-password tokens, sign-in cookies.

public static class AuthSetup
{
    public const string CookieScheme = "hg";
    public const string ExternalScheme = "hg.ext";
    public const string OidcScheme = "oidc";
}

public enum LoginOutcome { Success, Failed, NeedsTotp, LocalLoginDisabled }

public sealed class AccountService(AppDb db, ConfigProvider config, IHttpContextAccessor http)
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan LockoutTime = TimeSpan.FromMinutes(15);
    private static readonly PasswordHasher<User> Hasher = new(Options.Create(new PasswordHasherOptions { IterationCount = 600_000 }));
    private static readonly string DummyHash = Hasher.HashPassword(new User(), "dummy-password-for-timing");

    public static string Normalize(string username) => username.Trim().ToUpperInvariant();

    public static string? ValidateUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username)) return "Enter a username.";
        var u = username.Trim();
        if (u.Length is < 3 or > 32) return "The username must have 3 to 32 characters.";
        if (!u.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.')) return "Use only letters, digits, '.', '_' and '-'.";
        return null;
    }

    public static string? ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 12) return "The password must have at least 12 characters.";
        if (password.Length > 256) return "The password is too long.";
        return null;
    }

    public void SetPassword(User user, string password)
    {
        user.PasswordHash = Hasher.HashPassword(user, password);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
    }

    public async Task<(LoginOutcome Outcome, User? User)> Login(string username, string password, string? totp)
    {
        if (!config.Current.Auth.LocalLogin) return (LoginOutcome.LocalLoginDisabled, null);
        var norm = Normalize(username ?? "");
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedUsername == norm);
        if (user is null || user.PasswordHash is null)
        {
            Hasher.VerifyHashedPassword(new User(), DummyHash, password ?? ""); // equal timing
            return (LoginOutcome.Failed, null);
        }
        var now = DateTimeOffset.UtcNow;
        if (user.LockoutUntil > now || user.Disabled)
        {
            Hasher.VerifyHashedPassword(user, DummyHash, password ?? "");
            return (LoginOutcome.Failed, user);
        }
        var result = Hasher.VerifyHashedPassword(user, user.PasswordHash, password ?? "");
        if (result == PasswordVerificationResult.Failed)
        {
            await RegisterFailure(user);
            return (LoginOutcome.Failed, user);
        }
        if (result == PasswordVerificationResult.SuccessRehashNeeded) user.PasswordHash = Hasher.HashPassword(user, password!);
        if (user.TotpEnabled)
        {
            if (string.IsNullOrWhiteSpace(totp)) { await db.SaveChangesAsync(); return (LoginOutcome.NeedsTotp, user); }
            if (!VerifyTotp(user, totp)) { await RegisterFailure(user); return (LoginOutcome.Failed, user); }
        }
        user.FailedLogins = 0;
        user.LockoutUntil = null;
        user.LastLoginAt = now;
        await db.SaveChangesAsync();
        return (LoginOutcome.Success, user);
    }

    private async Task RegisterFailure(User user)
    {
        user.FailedLogins++;
        if (user.FailedLogins >= MaxFailures)
        {
            user.LockoutUntil = DateTimeOffset.UtcNow + LockoutTime;
            user.FailedLogins = 0;
        }
        await db.SaveChangesAsync();
    }

    public static bool VerifyTotp(User user, string code, string? secretOverride = null)
    {
        var secret = secretOverride ?? user.TotpSecret;
        if (secret is null) return false;
        var totp = new Totp(Base32Encoding.ToBytes(secret));
        if (!totp.VerifyTotp(code.Trim().Replace(" ", ""), out var step, new VerificationWindow(1, 1))) return false;
        if (step <= user.TotpLastStep) return false; // no code reuse
        user.TotpLastStep = step;
        return true;
    }

    public static string NewTotpSecret() => Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));

    public async Task SignIn(User user, bool viaExternal = false)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(UserContextMiddleware.StampClaim, user.SecurityStamp),
            new("hg:method", viaExternal ? "oidc" : "local"),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthSetup.CookieScheme));
        await http.HttpContext!.SignInAsync(AuthSetup.CookieScheme, principal, new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(Math.Clamp(config.Current.Auth.SessionHours, 1, 24 * 30)),
            AllowRefresh = true,
        });
    }

    public async Task<string> CreateSetupToken(User user)
    {
        var raw = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        await db.SetupTokens.Where(t => t.UserId == user.Id && !t.Used).ExecuteUpdateAsync(s => s.SetProperty(t => t.Used, true));
        db.SetupTokens.Add(new SetupToken { TokenHash = Sha256(raw), UserId = user.Id, ExpiresAt = DateTimeOffset.UtcNow.AddHours(72) });
        await db.SaveChangesAsync();
        return raw;
    }

    public async Task<User?> ConsumeSetupToken(string raw)
    {
        var hash = Sha256(raw ?? "");
        var token = await db.SetupTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
        if (token is null || token.Used || token.ExpiresAt < DateTimeOffset.UtcNow) return null;
        token.Used = true;
        return await db.Users.FirstOrDefaultAsync(u => u.Id == token.UserId && !u.Disabled);
    }

    public static string Sha256(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    public static string Base64UrlEncode(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
