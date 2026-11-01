using HearthGate.Contracts;
using HearthGate.Server.Agents;
using HearthGate.Server.Auth;
using HearthGate.Server.Config;

namespace HearthGate.Server.Endpoints;

// Owns: game-server views/control and host health views. Permission checks happen here on the server.
public static class OperationsEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/games", (HttpContext ctx, ConfigProvider config, AgentCache cache) =>
        {
            // Guests get name, game and a coarse state only. games.view adds players, version and the connect address.
            var u = ctx.Hg();
            var details = u.Has(Permissions.GamesView);
            return Results.Ok(config.Current.GameServers.Where(g => u.CanSee(g.Visibility)).Select(g =>
            {
                cache.Games.TryGetValue($"{g.Host}/{g.AgentKey}", out var entry);
                var stale = entry.Status is null || DateTimeOffset.UtcNow - entry.At > TimeSpan.FromMinutes(2);
                var state = stale ? GameStates.Unknown : entry.Status!.State;
                if (!details && state is GameStates.Starting or GameStates.Stopping) state = GameStates.Unknown;
                return new
                {
                    g.Id, g.Name, g.Game, g.Icon,
                    ConnectInfo = details || g.ConnectInfoPublic ? g.ConnectInfo : null,
                    State = state,
                    Players = stale || !details ? null : entry.Status!.Players,
                    PlayerNames = stale || !details ? null : entry.Status!.PlayerNames,
                    Version = stale || !details ? null : entry.Status!.Version,
                    UpdatedAt = stale ? (DateTimeOffset?)null : entry.At,
                    CanControl = u.Has(Permissions.GamesControl(g.Id)),
                };
            }));
        });

        app.MapPost("/api/games/{id}/{action}", async (string id, string action, HttpContext ctx, ConfigProvider config,
            AgentClient client, AgentPoller poller, AuditService audit, IServiceScopeFactory scopes, ILogger<AgentClient> log) =>
        {
            var u = ctx.Hg();
            var game = config.Current.GameServers.FirstOrDefault(g => g.Id == id);
            if (game is null || !GameActions.All.Contains(action) || !u.CanSee(game.Visibility)) return Results.NotFound();
            if (!u.Has(Permissions.GamesControl(game.Id))) return Results.Forbid();
            await audit.Log($"game.{action}", game.Id, "requested");
            // A graceful stop can take minutes (longer than the Cloudflare request limit), so it runs in the background.
            var actor = u.Username;
            _ = Task.Run(async () =>
            {
                GameActionResult result;
                try { result = await client.GameAction(game.Host, game.AgentKey, action, CancellationToken.None); }
                catch (Exception ex) { log.LogWarning("Game action failed: {Message}", ex.Message); result = new GameActionResult(false, "The host agent is not reachable."); }
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<HearthGate.Server.Data.AppDb>();
                db.AuditEvents.Add(new HearthGate.Server.Data.AuditEvent
                {
                    Action = $"game.{action}", ActorUserId = u.Id, ActorName = actor, Target = game.Id, Detail = result.Message, Success = result.Ok,
                });
                await db.SaveChangesAsync();
                poller.Kick();
            });
            poller.Kick();
            return Results.Json(new { ok = true, message = $"The {action} request was sent. The status updates in a short time." }, statusCode: 202);
        }).RequireRateLimiting("action");

        app.MapGet("/api/hosts", (HttpContext ctx, ConfigProvider config, AgentCache cache) =>
        {
            if (!ctx.Hg().Has(Permissions.HostsView)) return Results.Forbid();
            return Results.Ok(config.Current.Hosts.Select(h =>
            {
                cache.Hosts.TryGetValue(h.Id, out var entry);
                var online = entry.Snapshot is not null && DateTimeOffset.UtcNow - entry.At < TimeSpan.FromMinutes(2);
                return new HostView(h.Id, h.Name, online, entry.Snapshot is null ? null : entry.At, entry.Snapshot);
            }));
        });
    }
}
