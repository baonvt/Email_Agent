using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Auth;
using Project_AI.Application.Interfaces;
using Project_AI.Domain.Entities;
using Project_AI.Infrastructure.Data;
using Project_AI.Infrastructure.Data.Identity;
using Project_AI.Infrastructure.Services;

namespace Project_AI.Infrastructure.Repositories;

public sealed class AuthSessionRepository(AuthDbContext db, UserManager<ApplicationUser> users, TimeProvider clock)
    : IAuthSessionRepository
{
    public async Task<AuthSessionData> CreateAsync(AuthAccount account, string refreshTokenHash,
        DateTimeOffset expiresAt, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await LockUserAsync(account.Profile.Id, ct);
        if (user is null) throw InvalidSession();
        // The earlier password check can be tracked in this scope. Read the locked database state.
        await db.Entry(user).ReloadAsync(ct);
        if (!user.EmailConfirmed || user.SecurityStamp != account.SecurityStamp || await users.IsLockedOutAsync(user))
            throw InvalidSession();
        var now = clock.GetUtcNow();
        var session = new AuthSession(user.Id, user.SecurityStamp!, now, expiresAt);
        db.AuthSessions.Add(session);
        db.RefreshTokens.Add(new RefreshToken(session.Id, refreshTokenHash, now, expiresAt));
        var result = new AuthSessionData(session.Id, expiresAt, await AccountAsync(user));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<AuthSessionData> RotateAsync(string currentTokenHash, string replacementTokenHash, CancellationToken ct)
    {
        var known = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == currentTokenHash, ct);
        if (known is null) throw InvalidSession();
        var owner = await db.AuthSessions.AsNoTracking().Where(x => x.Id == known.SessionId)
            .Select(x => x.UserId).SingleAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Account/session mutations always lock user first, then session.
        var user = await LockUserAsync(owner, ct);
        var session = (await db.AuthSessions.FromSqlInterpolated(
            $"SELECT * FROM \"AuthSessions\" WHERE \"Id\" = {known.SessionId} FOR UPDATE")
            .ToListAsync(ct)).SingleOrDefault();
        if (user is null || session is null) throw InvalidSession();
        var stored = await db.RefreshTokens.SingleAsync(x => x.Id == known.Id, ct);
        var now = clock.GetUtcNow();
        if (stored.ConsumedAt is not null)
        {
            session.Revoke(now);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            throw InvalidSession();
        }
        if (!stored.CanUse(now) || !session.IsActive(now) || !user.EmailConfirmed
            || session.SecurityStamp != user.SecurityStamp || await users.IsLockedOutAsync(user))
            throw InvalidSession();
        stored.Consume(now);
        db.RefreshTokens.Add(new RefreshToken(session.Id, replacementTokenHash, now, session.ExpiresAt));
        var result = new AuthSessionData(session.Id, session.ExpiresAt, await AccountAsync(user));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task RevokeAsync(AccessTokenContext context, bool allSessions, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await LockUserAsync(context.UserId, ct);
        if (user is null) throw InvalidSession();
        var now = clock.GetUtcNow();
        await db.AuthSessions.Where(x => x.UserId == user.Id && x.RevokedAt == null
                && (allSessions || x.Id == context.SessionId))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
        if (allSessions) IdentityAccountService.Ensure(await users.UpdateSecurityStampAsync(user), ErrorCode.LogoutFailed);
        await transaction.CommitAsync(ct);
    }

    public Task<bool> IsActiveAsync(AccessTokenContext context, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return (from session in db.AuthSessions.AsNoTracking()
                join user in db.Users.AsNoTracking() on session.UserId equals user.Id
                where session.Id == context.SessionId && session.UserId == context.UserId
                    && session.RevokedAt == null && session.ExpiresAt > now && user.EmailConfirmed
                    && session.SecurityStamp == context.SecurityStamp && user.SecurityStamp == context.SecurityStamp
                    && (!user.LockoutEnabled || user.LockoutEnd == null || user.LockoutEnd <= now)
                select session.Id).AnyAsync(ct);
    }

    private async Task<ApplicationUser?> LockUserAsync(Guid id, CancellationToken ct) =>
        (await db.Users.FromSqlInterpolated(
            $"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(ct)).SingleOrDefault();
    private async Task<AuthAccount> AccountAsync(ApplicationUser user) => new(
        new UserProfile(user.Id, user.Email!, user.DisplayName, (await users.GetRolesAsync(user)).ToArray()),
        user.SecurityStamp!, user.EmailConfirmed);
    private static AppException InvalidSession() => new(ErrorCode.InvalidSession,
        "The session is invalid or expired. Please sign in again.");
}
