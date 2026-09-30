using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;



namespace Project_AI.Infrastructure.Persistence;

public static class AuthDatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, bool migrate, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        if (migrate) await db.Database.MigrateAsync(ct);
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var name in new[] { AuthRoles.User, AuthRoles.Admin })
        {
            if (!await roles.RoleExistsAsync(name))
            {
                var result = await roles.CreateAsync(new IdentityRole<Guid>(name));
                // Another instance may have seeded the same role concurrently.
                if (!result.Succeeded && !await roles.RoleExistsAsync(name))
                    throw new InvalidOperationException("Could not initialize auth roles.");
            }
        }
    }

    public static async Task PromoteAdminAsync(IServiceProvider services, string email, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var normalized = users.NormalizeEmail(email.Trim());
        var user = (await db.Users.FromSqlInterpolated(
            $"SELECT * FROM \"AspNetUsers\" WHERE \"NormalizedEmail\" = {normalized} FOR UPDATE").ToListAsync(ct)).SingleOrDefault()
            ?? throw new InvalidOperationException("Register the account before promoting it.");
        if (!user.EmailConfirmed) throw new InvalidOperationException("Confirm the account's email first.");
        IdentityAccounts.Ensure(await users.AddToRoleAsync(user, AuthRoles.Admin), "role_assignment_failed");
        IdentityAccounts.Ensure(await users.UpdateSecurityStampAsync(user), "role_assignment_failed");
        var now = DateTimeOffset.UtcNow;
        await db.AuthSessions.Where(x => x.UserId == user.Id && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
        await transaction.CommitAsync(ct);
    }
}
