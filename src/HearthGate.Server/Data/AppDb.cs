using Microsoft.EntityFrameworkCore;

namespace HearthGate.Server.Data;

// Owns: persistent state (users, groups, requests, tokens, links, audit, status samples).

public sealed class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string NormalizedUsername { get; set; } = "";
    public string? Email { get; set; }
    public string? PasswordHash { get; set; }
    public bool IsAdmin { get; set; }
    public bool Disabled { get; set; }
    public string? TotpSecret { get; set; }
    public bool TotpEnabled { get; set; }
    public long TotpLastStep { get; set; }
    public int FailedLogins { get; set; }
    public DateTimeOffset? LockoutUntil { get; set; }
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }
    public List<UserGroup> Groups { get; set; } = [];
    public List<ExternalLogin> ExternalLogins { get; set; } = [];
}

public sealed class Group
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Permissions { get; set; } = ""; // space separated
    public List<UserGroup> Users { get; set; } = [];

    public IEnumerable<string> PermissionList() =>
        Permissions.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed class UserGroup
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;
}

public static class RequestStatus
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

public sealed class AccountRequest
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string? Email { get; set; }
    public string? Message { get; set; }
    public string Status { get; set; } = RequestStatus.Pending;
    public string? ExternalIssuer { get; set; }
    public string? ExternalSubject { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DecidedAt { get; set; }
}

public sealed class SetupToken
{
    public int Id { get; set; }
    public string TokenHash { get; set; } = "";
    public int UserId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Used { get; set; }
}

public sealed class ExternalLogin
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string Provider { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string Subject { get; set; } = "";
    public string? DisplayName { get; set; }
    public DateTimeOffset LinkedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AuditEvent
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = "";
    public int? ActorUserId { get; set; }
    public string? ActorName { get; set; }
    public string? Target { get; set; }
    public string? Detail { get; set; }
    public string? Ip { get; set; }
    public bool Success { get; set; } = true;
}

public sealed class StatusSample
{
    public long Id { get; set; }
    public string ServiceId { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public string State { get; set; } = "";
    public int? LatencyMs { get; set; }
}

public sealed class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<UserGroup> UserGroups => Set<UserGroup>();
    public DbSet<AccountRequest> AccountRequests => Set<AccountRequest>();
    public DbSet<SetupToken> SetupTokens => Set<SetupToken>();
    public DbSet<ExternalLogin> ExternalLogins => Set<ExternalLogin>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<StatusSample> StatusSamples => Set<StatusSample>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(u => u.NormalizedUsername).IsUnique();
        b.Entity<Group>().HasIndex(g => g.Name).IsUnique();
        b.Entity<UserGroup>().HasKey(x => new { x.UserId, x.GroupId });
        b.Entity<ExternalLogin>().HasIndex(x => new { x.Issuer, x.Subject }).IsUnique();
        b.Entity<SetupToken>().HasIndex(x => x.TokenHash).IsUnique();
        b.Entity<AuditEvent>().HasIndex(x => x.At);
        b.Entity<StatusSample>().HasIndex(x => new { x.ServiceId, x.At });
    }

    // SQLite cannot compare or order DateTimeOffset; store it as a sortable long.
    protected override void ConfigureConventions(ModelConfigurationBuilder c) =>
        c.Properties<DateTimeOffset>().HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
}
