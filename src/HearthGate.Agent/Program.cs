using System.Net;
using System.Security.Cryptography;
using System.Text;
using HearthGate.Agent;
using HearthGate.Contracts;

// HearthGate host agent. Listens on the private network only. Every request needs the bearer token.
// Endpoints: GET /v1/host, GET /v1/games, POST /v1/games/{key}/{start|stop|restart}.

var config = AgentConfig.Load();
var token = Environment.GetEnvironmentVariable(config.TokenEnv);
if (string.IsNullOrEmpty(token) || token.Length < 24)
{
    Console.Error.WriteLine($"Set the environment variable {config.TokenEnv} to a random token of at least 24 characters.");
    return 1;
}

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(o => o.ServiceName = "HearthGateAgent");
builder.WebHost.UseUrls(config.Listen);
builder.WebHost.ConfigureKestrel(k => k.AddServerHeader = false);
builder.Services.AddSingleton(config);
builder.Services.AddSingleton<HostReader>();
builder.Services.AddSingleton<GameController>();
builder.Services.AddHttpClient("docker", c => c.Timeout = TimeSpan.FromSeconds(70));
builder.Services.AddHttpClient("game", c => c.Timeout = TimeSpan.FromSeconds(10));

var app = builder.Build();

var allowed = config.AllowedClients.Select(c => System.Net.IPNetwork.Parse(c.Contains('/') ? c : c + (c.Contains(':') ? "/128" : "/32"))).ToList();
var tokenBytes = Encoding.UTF8.GetBytes("Bearer " + token);

app.Use(async (ctx, next) =>
{
    var ip = ctx.Connection.RemoteIpAddress;
    if (ip is { IsIPv4MappedToIPv6: true }) ip = ip.MapToIPv4();
    if (allowed.Count > 0 && (ip is null || !allowed.Any(n => n.Contains(ip))))
    {
        ctx.Response.StatusCode = 403;
        return;
    }
    var header = Encoding.UTF8.GetBytes(ctx.Request.Headers.Authorization.ToString());
    if (!CryptographicOperations.FixedTimeEquals(header, tokenBytes))
    {
        await Task.Delay(200);
        ctx.Response.StatusCode = 401;
        return;
    }
    await next();
});

app.MapGet("/v1/host", (HostReader reader, CancellationToken ct) => reader.Read(ct));
app.MapGet("/v1/games", (GameController games, CancellationToken ct) => games.All(ct));
app.MapPost("/v1/games/{key}/{action}", async (string key, string action, GameController games, CancellationToken ct) =>
{
    var g = games.Find(key);
    if (g is null || !GameActions.All.Contains(action)) return Results.NotFound(new GameActionResult(false, "Unknown server or action."));
    var result = await games.Act(g, action, ct);
    return Results.Json(result, statusCode: result.Ok ? 200 : (int)HttpStatusCode.Conflict);
});

app.Run();
return 0;
