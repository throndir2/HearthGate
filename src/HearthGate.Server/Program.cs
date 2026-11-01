using System.Net;
using System.Threading.RateLimiting;
using HearthGate.Server.Agents;
using HearthGate.Server.Auth;
using HearthGate.Server.Config;
using HearthGate.Server.Data;
using HearthGate.Server.Endpoints;
using HearthGate.Server.GitHub;
using HearthGate.Server.Security;
using HearthGate.Server.Status;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(k => k.AddServerHeader = false);

var dataDir = Environment.GetEnvironmentVariable("HG_DATA") ?? Path.Combine(builder.Environment.ContentRootPath, ".hgdata");
Directory.CreateDirectory(dataDir);
var insecureCookies = string.Equals(Environment.GetEnvironmentVariable("HG_INSECURE_COOKIES"), "true", StringComparison.OrdinalIgnoreCase);
var cookiePrefix = insecureCookies ? "" : "__Host-";
var cookieSecure = insecureCookies ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

// Config is needed at startup for OIDC and proxy settings; the provider also hot-reloads.
var configProvider = new ConfigProvider(
    LoggerFactory.Create(l => l.AddConsole()).CreateLogger<ConfigProvider>(), builder.Environment);
builder.Services.AddSingleton(configProvider);
var startupCfg = configProvider.Current;

builder.Services.AddDbContext<AppDb>(o => o.UseSqlite($"Data Source={Path.Combine(dataDir, "hearthgate.db")}"));
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys"))).SetApplicationName("HearthGate");
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddSingleton<ProofOfWorkService>();
builder.Services.AddSingleton<StatusStore>();
builder.Services.AddHostedService<StatusMonitor>();
builder.Services.AddSingleton<GitHubShowcaseService>();
builder.Services.AddSingleton<AgentClient>();
builder.Services.AddSingleton<AgentCache>();
builder.Services.AddSingleton<AgentPoller>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AgentPoller>());

const string userAgent = "HearthGate/1.0 (+https://github.com/throndir2/HearthGate)";
builder.Services.AddHttpClient("status", c => c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false, MaxAutomaticRedirections = 5 });
builder.Services.AddHttpClient("status-insecure", c => c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        UseCookies = false,
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
    });
builder.Services.AddHttpClient("github", c =>
{
    c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
    c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    c.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHttpClient("agent", c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHttpClient("agent-action", c => c.Timeout = TimeSpan.FromSeconds(660));

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Walk back through every trusted hop (for example Cloudflare -> NPM) to the first untrusted address.
    o.ForwardLimit = startupCfg.Security.TrustedProxies.Count > 0 ? null : 1;
    o.KnownProxies.Clear();
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Add(IPAddress.Loopback);
    foreach (var p in startupCfg.Security.TrustedProxies)
    {
        var cidr = p.Contains('/') ? p : p + (p.Contains(':') ? "/128" : "/32");
        o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
    }
});

builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF-TOKEN";
    o.Cookie.Name = cookiePrefix + "hg.af";
    o.Cookie.SecurePolicy = cookieSecure;
    o.Cookie.SameSite = SameSiteMode.Strict;
});

var authBuilder = builder.Services.AddAuthentication(AuthSetup.CookieScheme)
    .AddCookie(AuthSetup.CookieScheme, o =>
    {
        o.Cookie.Name = cookiePrefix + "hg";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = cookieSecure;
        o.SlidingExpiration = true;
        o.ExpireTimeSpan = TimeSpan.FromHours(Math.Clamp(startupCfg.Auth.SessionHours, 1, 24 * 30));
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
    })
    .AddCookie(AuthSetup.ExternalScheme, o =>
    {
        o.Cookie.Name = cookiePrefix + "hg.ext";
        o.Cookie.SecurePolicy = cookieSecure;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromMinutes(5);
    });

var oidcRegistered = false;
if (startupCfg.Auth.Oidc is { Enabled: true } oidc && !string.IsNullOrWhiteSpace(oidc.Authority))
{
    oidcRegistered = true;
    authBuilder.AddOpenIdConnect(AuthSetup.OidcScheme, o =>
    {
        o.SignInScheme = AuthSetup.ExternalScheme;
        o.Authority = oidc.Authority;
        o.ClientId = oidc.ClientId;
        o.ClientSecret = ConfigProvider.Secret(oidc.ClientSecretEnv);
        o.ResponseType = OpenIdConnectResponseType.Code;
        o.ResponseMode = OpenIdConnectResponseMode.Query;
        o.UsePkce = true;
        o.RequireHttpsMetadata = !insecureCookies; // plain HTTP only in local development
        o.MapInboundClaims = false;
        o.GetClaimsFromUserInfoEndpoint = true;
        o.SaveTokens = false;
        o.CallbackPath = "/signin-oidc";
        o.Scope.Clear();
        foreach (var s in new[] { "openid", "profile", "email" }) o.Scope.Add(s);
        o.CorrelationCookie.SecurePolicy = cookieSecure;
        o.NonceCookie.SecurePolicy = cookieSecure;
        // The provider returns with a top-level GET (response_mode=query), so Lax is enough and stricter than None.
        o.CorrelationCookie.SameSite = SameSiteMode.Lax;
        o.NonceCookie.SameSite = SameSiteMode.Lax;
        o.Events.OnRemoteFailure = ctx => { ctx.Response.Redirect("/signin?error=oidc"); ctx.HandleResponse(); return Task.CompletedTask; };
    });
}
builder.Services.AddSingleton(new OidcState(oidcRegistered));

static string ClientKey(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
static RateLimitPartition<string> Window(HttpContext ctx, int permits, TimeSpan window, string? key = null) =>
    RateLimitPartition.GetFixedWindowLimiter(key ?? ClientKey(ctx), _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = window });

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.OnRejected = async (ctx, ct) => await ctx.HttpContext.Response.WriteAsJsonAsync(new { error = "Too many requests. Wait a short time and try again." }, ct);
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => Window(ctx, 300, TimeSpan.FromMinutes(1)));
    o.AddPolicy("auth", ctx => Window(ctx, 10, TimeSpan.FromMinutes(1)));
    o.AddPolicy("request", ctx => Window(ctx, 3, TimeSpan.FromMinutes(15)));
    o.AddPolicy("pow", ctx => Window(ctx, 30, TimeSpan.FromMinutes(10)));
    o.AddPolicy("action", ctx => Window(ctx, 20, TimeSpan.FromMinutes(1), "u" + (ctx.Hg().Id?.ToString() ?? ClientKey(ctx))));
});

var app = builder.Build();

await Bootstrap.Run(app.Services, app.Logger, dataDir);

app.UseForwardedHeaders();
app.Use(SecurityHeaders.Middleware(configProvider));
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler(e => e.Run(async ctx =>
{
    ctx.Response.StatusCode = 500;
    await ctx.Response.WriteAsJsonAsync(new { error = "Something went wrong." });
}));

var iconsDir = Path.Combine(dataDir, "icons");
Directory.CreateDirectory(iconsDir);
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.Context.Request.Path.Value ?? "";
        ctx.Context.Response.Headers.CacheControl = path.StartsWith("/assets/") ? "public, max-age=31536000, immutable" : "no-cache";
    },
});
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(iconsDir), RequestPath = "/icons" });

app.UseAuthentication();
app.UseMiddleware<UserContextMiddleware>();
app.UseRateLimiter();

// CSRF: every state-changing API call needs the antiforgery token (header or form field).
app.Use(async (ctx, next) =>
{
    var m = ctx.Request.Method;
    if (ctx.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(m) && !HttpMethods.IsHead(m) && !HttpMethods.IsOptions(m))
    {
        try { await ctx.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(ctx); }
        catch (AntiforgeryValidationException)
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(new { error = "The page is out of date. Reload and try again." });
            return;
        }
    }
    await next();
});

PublicEndpoints.Map(app);
AccountEndpoints.Map(app);
OperationsEndpoints.Map(app);
AdminEndpoints.Map(app);
OidcEndpoints.Map(app);
app.MapGet("/healthz", () => Results.Text("ok"));
app.Map("/api/{**rest}", () => Results.NotFound());
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
});

app.Run();
