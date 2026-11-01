using HearthGate.Server.Config;

namespace HearthGate.Server.Security;

// Owns: response security headers (CSP, HSTS, framing, sniffing, referrer, permissions).
public static class SecurityHeaders
{
    public static Func<HttpContext, RequestDelegate, Task> Middleware(ConfigProvider config) => async (ctx, next) =>
    {
        var h = ctx.Response.Headers;
        var formAction = "'self'";
        if (config.Current.Auth.Oidc is { Enabled: true } o && Uri.TryCreate(o.Authority, UriKind.Absolute, out var a))
            formAction += " " + a.GetLeftPart(UriPartial.Authority); // the link form redirects to the provider
        h.ContentSecurityPolicy =
            "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; " +
            $"connect-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action {formAction}";
        h.XContentTypeOptions = "nosniff";
        h.XFrameOptions = "DENY";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
        h["Cross-Origin-Opener-Policy"] = "same-origin";
        h["Cross-Origin-Resource-Policy"] = "same-origin";
        if (ctx.Request.IsHttps) h.StrictTransportSecurity = "max-age=31536000";
        if (ctx.Request.Path.StartsWithSegments("/api")) h.CacheControl = "no-store";
        await next(ctx);
    };
}
