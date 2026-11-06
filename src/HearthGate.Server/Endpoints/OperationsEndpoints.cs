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
                    // Only the commands that this user may run. Guests and users without control get none.
                    Commands = entry.Status?.Commands?.Where(c => u.Has(c.Level == GameCommandLevels.Control
                        ? Permissions.GamesControl(g.Id) : Permissions.GamesAdmin(g.Id))).ToList() is { Count: > 0 } cmds ? cmds : null,
                };
            }));
        });

        app.MapPost("/api/games/{id}/commands/{commandId}", async (string id, string commandId, GameCommandRequest? body, HttpContext ctx,
            ConfigProvider config, AgentCache cache, AgentClient client, AuditService audit, ILogger<AgentClient> log) =>
        {
            var u = ctx.Hg();
            var game = config.Current.GameServers.FirstOrDefault(g => g.Id == id);
            if (game is null || !u.CanSee(game.Visibility)) return Results.NotFound();
            if (!u.Has(Permissions.GamesControl(game.Id))) return Results.Forbid();
            cache.Games.TryGetValue($"{game.Host}/{game.AgentKey}", out var entry);
            var cmd = entry.Status?.Commands?.FirstOrDefault(c => string.Equals(c.Id, commandId, StringComparison.OrdinalIgnoreCase));
            if (cmd is null) return Results.NotFound();
            if (cmd.Level != GameCommandLevels.Control && !u.Has(Permissions.GamesAdmin(game.Id))) return Results.Forbid();
            var parameters = (body?.Params ?? []).Where(p => cmd.Params.Any(d => d.Name == p.Key))
                .ToDictionary(p => p.Key, p => p.Value ?? "");
            if (parameters.Values.Any(v => v.Length > 1000)) return Results.BadRequest(new { error = "A value is too long." });

            GameCommandResult result;
            try { result = await client.GameCommand(game.Host, game.AgentKey, cmd.Id, parameters, ctx.RequestAborted); }
            catch (Exception ex) when (!ctx.RequestAborted.IsCancellationRequested)
            {
                log.LogWarning("Game command failed: {Message}", ex.Message);
                result = new GameCommandResult(false, "The host agent is not reachable.", null);
            }
            var args = string.Join(", ", parameters.Select(p => $"{p.Key}={p.Value}"));
            await audit.Log("game.command", game.Id, $"{cmd.Id}{(args.Length > 0 ? $" ({args})" : "")}: {result.Message}", result.Ok);
            return Results.Ok(new { result.Ok, result.Message, result.Output });
        }).RequireRateLimiting("action");

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

        app.MapGet("/api/hosts", (HttpContext ctx, ConfigProvider config, AgentCache cache, HearthGate.Server.Status.HostProbeStore probes) =>
        {
            if (!ctx.Hg().Has(Permissions.HostsView)) return Results.Forbid();
            return Results.Ok(config.Current.Hosts.Select(h =>
            {
                cache.Hosts.TryGetValue(h.Id, out var entry);
                probes.Items.TryGetValue(h.Id, out var network);
                var online = entry.Snapshot is not null && DateTimeOffset.UtcNow - entry.At < TimeSpan.FromMinutes(2);
                // Links to the host's services, for example through Tailscale. Only users with hosts.view see them.
                var linkBase = string.IsNullOrWhiteSpace(h.LinkAddress) ? null : $"{h.LinkScheme}://{h.LinkAddress.Trim()}";
                var links = h.Links.Select(l => new LinkConfig
                {
                    Label = l.Label,
                    Url = linkBase is not null && (l.Url.StartsWith(':') || l.Url.StartsWith('/')) ? linkBase + l.Url : l.Url,
                }).ToList();
                return new HostView(h.Id, h.Name, !string.IsNullOrWhiteSpace(h.AgentUrl), online, entry.Snapshot is null ? null : entry.At,
                    entry.Snapshot, h.Address, network, linkBase, links);
            }));
        });
    }
}
