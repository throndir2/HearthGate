using HearthGate.Server.Auth;
using HearthGate.Server.Config;
using HearthGate.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace HearthGate.Server.Data;

// Owns: one-time startup work: create the database, seed groups, and create the first admin.
// Runs once per process start. Safe to run many times (it only adds what is missing).
public static class Bootstrap
{
    public static async Task Run(IServiceProvider services, ILogger log, string dataDir)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var config = scope.ServiceProvider.GetRequiredService<ConfigProvider>().Current;
        await db.Database.EnsureCreatedAsync();

        var seeds = config.Auth.Groups.Count > 0 ? config.Auth.Groups :
        [
            new GroupSeedConfig { Name = "Members", Permissions = [] },
            new GroupSeedConfig { Name = "Trusted", Permissions = [Permissions.StatusDetails, Permissions.ServicesPrivate, Permissions.GamesView] },
            new GroupSeedConfig { Name = "Game hosts", Permissions = [Permissions.GamesView, Permissions.GamesControlAll] },
        ];
        foreach (var g in seeds.Where(g => !string.IsNullOrWhiteSpace(g.Name)))
            if (!await db.Groups.AnyAsync(x => x.Name == g.Name))
                db.Groups.Add(new Group { Name = g.Name, Permissions = string.Join(' ', g.Permissions) });
        await db.SaveChangesAsync();

        if (await db.Users.AnyAsync()) return;
        var name = Environment.GetEnvironmentVariable("HG_ADMIN_USER") ?? "admin";
        var password = Environment.GetEnvironmentVariable("HG_ADMIN_PASSWORD");
        var admin = new User { Username = name, NormalizedUsername = AccountService.Normalize(name), IsAdmin = true };
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        if (!string.IsNullOrEmpty(password) && AccountService.ValidatePassword(password) is null)
        {
            accounts.SetPassword(admin, password);
            await db.SaveChangesAsync();
            log.LogWarning("Created admin '{Name}' from HG_ADMIN_PASSWORD. Remove the variable now.", name);
        }
        else
        {
            var token = await accounts.CreateSetupToken(admin);
            var file = Path.Combine(dataDir, "first-admin-link.txt");
            await File.WriteAllTextAsync(file, $"/set-password?token={token}\nThis link works once and expires in 72 hours. Delete this file after use.\n");
            log.LogWarning("Created admin '{Name}'. The one-time set-password link is in {File}.", name, file);
        }
    }
}
