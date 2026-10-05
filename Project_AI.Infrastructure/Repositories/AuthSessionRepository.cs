using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Auth;
using Project_AI.Domain.Entities;
using Project_AI.Infrastructure.Data;
using Project_AI.Infrastructure.Data.Identity;
using Project_AI.Infrastructure.Models;

namespace Project_AI.Infrastructure.Repositories;

public sealed class AuthSessionRepository
{
    private readonly AuthDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly TimeProvider _timeProvider;

    public AuthSessionRepository(
        AuthDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _timeProvider = timeProvider;
    }

    public async Task<AuthSessionData> CreateAsync(AuthAccount account, string refreshTokenHash,
        DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var user = await LockUserAsync(account.Profile.Id, cancellationToken);
        if (user is null)
        {
            throw InvalidSession();
        }
        // The earlier password check can be tracked in this scope. Read the locked database state.
        await _dbContext.Entry(user).ReloadAsync(cancellationToken);
        if (!user.EmailConfirmed || user.SecurityStamp != account.SecurityStamp || await _userManager.IsLockedOutAsync(user))
        {
            throw InvalidSession();
        }
        var now = _timeProvider.GetUtcNow();
        var session = new AuthSession(user.Id, user.SecurityStamp!, now, expiresAt);
        _dbContext.AuthSessions.Add(session);
        _dbContext.RefreshTokens.Add(new RefreshToken(session.Id, refreshTokenHash, now, expiresAt));
        var result = new AuthSessionData(session.Id, expiresAt, await AccountAsync(user));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<AuthSessionData> RotateAsync(string currentTokenHash, string replacementTokenHash, CancellationToken cancellationToken)
    {
        var known = await _dbContext.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == currentTokenHash, cancellationToken);
        if (known is null)
        {
            throw InvalidSession();
        }
        var owner = await _dbContext.AuthSessions.AsNoTracking().Where(x => x.Id == known.SessionId)
            .Select(x => x.UserId).SingleAsync(cancellationToken);
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        // Account/session mutations always lock user first, then session.
        var user = await LockUserAsync(owner, cancellationToken);
        var session = (await _dbContext.AuthSessions.FromSqlInterpolated(
            $"SELECT * FROM \"AuthSessions\" WHERE \"Id\" = {known.SessionId} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault();
        if (user is null || session is null)
        {
            throw InvalidSession();
        }
        var stored = await _dbContext.RefreshTokens.SingleAsync(x => x.Id == known.Id, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        if (stored.ConsumedAt is not null)
        {
            session.Revoke(now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw InvalidSession();
        }
        if (!stored.CanUse(now) || !session.IsActive(now) || !user.EmailConfirmed
            || session.SecurityStamp != user.SecurityStamp || await _userManager.IsLockedOutAsync(user))
            throw InvalidSession();
        stored.Consume(now);
        _dbContext.RefreshTokens.Add(new RefreshToken(session.Id, replacementTokenHash, now, session.ExpiresAt));
        var result = new AuthSessionData(session.Id, session.ExpiresAt, await AccountAsync(user));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task RevokeAsync(AccessTokenContext context, bool allSessions, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var user = await LockUserAsync(context.UserId, cancellationToken);
        if (user is null)
        {
            throw InvalidSession();
        }
        var now = _timeProvider.GetUtcNow();
        await _dbContext.AuthSessions.Where(x => x.UserId == user.Id && x.RevokedAt == null
                && (allSessions || x.Id == context.SessionId))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), cancellationToken);
        if (allSessions)
        {
            var result = await _userManager.UpdateSecurityStampAsync(user);
            if (!result.Succeeded)
            {
                throw new AppException(ErrorCode.LogoutFailed, "Could not update the account security stamp.");
            }
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public Task<bool> IsActiveAsync(AccessTokenContext context, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        return (from session in _dbContext.AuthSessions.AsNoTracking()
                join user in _dbContext.Users.AsNoTracking() on session.UserId equals user.Id
                where session.Id == context.SessionId && session.UserId == context.UserId
                    && session.RevokedAt == null && session.ExpiresAt > now && user.EmailConfirmed
                    && session.SecurityStamp == context.SecurityStamp && user.SecurityStamp == context.SecurityStamp
                    && (!user.LockoutEnabled || user.LockoutEnd == null || user.LockoutEnd <= now)
                select session.Id).AnyAsync(cancellationToken);
    }

    private async Task<ApplicationUser?> LockUserAsync(Guid id, CancellationToken cancellationToken) =>
        (await _dbContext.Users.FromSqlInterpolated(
            $"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault();
    private async Task<AuthAccount> AccountAsync(ApplicationUser user) => new(
        new UserProfile(user.Id, user.Email!, user.DisplayName, (await _userManager.GetRolesAsync(user)).ToArray()),
        user.SecurityStamp!, user.EmailConfirmed);
    private static AppException InvalidSession() => new(ErrorCode.InvalidSession,
        "The session is invalid or expired. Please sign in again.");
}
