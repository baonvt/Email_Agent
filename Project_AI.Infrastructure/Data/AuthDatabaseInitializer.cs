using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Domain.Constants;
using Project_AI.Infrastructure.Data.Identity;

namespace Project_AI.Infrastructure.Data;

public static class AuthDatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, bool migrate, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        if (migrate) await db.Database.MigrateAsync(cancellationToken);
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var name in new[] { AuthRoles.User, AuthRoles.Admin })
        {
            if (!await roles.RoleExistsAsync(name))
            {
                var result = await roles.CreateAsync(new IdentityRole<Guid>(name));
                // Another instance may have seeded the same role concurrently.
                if (!result.Succeeded && !await roles.RoleExistsAsync(name))
                {
                    throw new InvalidOperationException("Could not initialize auth roles.");
                }
            }
        }
    }

    public static async Task PromoteAdminAsync(IServiceProvider services, string email, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var normalized = users.NormalizeEmail(email.Trim());
        var user = (await db.Users.FromSqlInterpolated(
            $"SELECT * FROM \"AspNetUsers\" WHERE \"NormalizedEmail\" = {normalized} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault()
            ?? throw new InvalidOperationException("Register the account before promoting it.");
        if (!user.EmailConfirmed)
        {
            throw new InvalidOperationException("Confirm the account's email first.");
        }
        var roleResult = await users.AddToRoleAsync(user, AuthRoles.Admin);
        if (!roleResult.Succeeded)
        {
            throw new AppException(ErrorCode.RoleAssignmentFailed, "Could not assign the Admin role.");
        }
        var stampResult = await users.UpdateSecurityStampAsync(user);
        if (!stampResult.Succeeded)
        {
            throw new AppException(ErrorCode.RoleAssignmentFailed, "Could not update the account security stamp.");
        }
        var now = DateTimeOffset.UtcNow;
        await db.AuthSessions.Where(x => x.UserId == user.Id && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
