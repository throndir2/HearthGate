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
            var u = ctx.Hg();
            if (!u.Has(Permissions.GamesView)) return Results.Forbid();
            return Results.Ok(config.Current.GameServers.Select(g =>
            {
                cache.Games.TryGetValue($"{g.Host}/{g.AgentKey}", out var entry);
                var stale = entry.Status is null || DateTimeOffset.UtcNow - entry.At > TimeSpan.FromMinutes(2);
                return new
                {
                    g.Id, g.Name, g.Game, g.Icon, g.ConnectInfo,
                    State = stale ? GameStates.Unknown : entry.Status!.State,
                    Players = stale ? null : entry.Status!.Players,
                    PlayerNames = stale ? null : entry.Status!.PlayerNames,
                    Version = stale ? null : entry.Status!.Version,
                    UpdatedAt = stale ? (DateTimeOffset?)null : entry.At,
                    CanControl = u.Has(Permissions.GamesControl(g.Id)),
                };
            }));
        });

        app.MapPost("/api/games/{id}/{action}", async (string id, string action, HttpContext ctx, ConfigProvider config,
            AgentClient client, AgentPoller poller, AuditService audit, CancellationToken ct) =>
        {
            var u = ctx.Hg();
            var game = config.Current.GameServers.FirstOrDefault(g => g.Id == id);
            if (game is null || !GameActions.All.Contains(action)) return Results.NotFound();
            if (!u.Has(Permissions.GamesControl(game.Id))) return Results.Forbid();
            GameActionResult result;
            try { result = await client.GameAction(game.Host, game.AgentKey, action, ct); }
            catch (Exception) { result = new GameActionResult(false, "The host agent is not reachable."); }
            await audit.Log($"game.{action}", game.Id, result.Message, result.Ok);
            poller.Kick();
            return Results.Json(new { ok = result.Ok, message = result.Message }, statusCode: result.Ok ? 200 : 502);
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
