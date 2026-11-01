using HearthGate.Server.Data;

namespace HearthGate.Server.Auth;

// Owns: writing audit events. Callers pass what happened; IP and actor come from the request.
public sealed class AuditService(AppDb db, IHttpContextAccessor http)
{
    public async Task Log(string action, string? target = null, string? detail = null, bool success = true, User? actorOverride = null)
    {
        var ctx = http.HttpContext;
        var uc = ctx?.Hg();
        db.AuditEvents.Add(new AuditEvent
        {
            Action = action,
            ActorUserId = actorOverride?.Id ?? uc?.Id,
            ActorName = actorOverride?.Username ?? uc?.Username,
            Target = Trim(target, 200),
            Detail = Trim(detail, 1000),
            Ip = ctx?.Connection.RemoteIpAddress?.ToString(),
            Success = success,
        });
        await db.SaveChangesAsync();
    }

    private static string? Trim(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}
