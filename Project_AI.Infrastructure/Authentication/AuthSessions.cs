using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;


using Project_AI.Infrastructure.Persistence;

namespace Project_AI.Infrastructure.Authentication;

public sealed class AuthSessions(AuthDbContext db, UserManager<ApplicationUser> users, JwtTokenIssuer issuer,
    TokenBlacklist blacklist, IOptions<JwtOptions> options, TimeProvider clock) : IAuthSessions
{
    public async Task<AuthTokens> CreateAsync(AuthAccount account, CancellationToken ct)
    {
        await blacklist.ContainsAsync("availability-check");
        // Serialize against password reset and logout-all; don't issue from a stale password check.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await LockUserAsync(account.Profile.Id, ct);
        if (user is null) throw InvalidSession();
        await db.Entry(user).ReloadAsync(ct);
        if (!user.EmailConfirmed || user.SecurityStamp != account.SecurityStamp || await users.IsLockedOutAsync(user))
            throw InvalidSession();
        var now = clock.GetUtcNow();
        var session = new AuthSession(user.Id, user.SecurityStamp!, now, now.AddDays(options.Value.RefreshTokenDays));
        var refresh = JwtTokenIssuer.NewRefreshToken();
        db.AuthSessions.Add(session);
        db.RefreshTokens.Add(new RefreshToken(session.Id, JwtTokenIssuer.HashRefreshToken(refresh), now, session.ExpiresAt));
        var result = issuer.Issue(await AccountAsync(user), session.Id, refresh, session.ExpiresAt);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<AuthTokens> RefreshAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256) throw InvalidSession();
        var hash = JwtTokenIssuer.HashRefreshToken(token);
        var known = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (known is null) throw InvalidSession();
        var owner = await db.AuthSessions.AsNoTracking().Where(x => x.Id == known.SessionId)
            .Select(x => x.UserId).SingleAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // All account/session mutations lock user first, then session, avoiding inconsistent lock order.
        var user = await LockUserAsync(owner, ct);
        var session = (await db.AuthSessions.FromSqlInterpolated(
            $"SELECT * FROM \"AuthSessions\" WHERE \"Id\" = {known.SessionId} FOR UPDATE")
            .ToListAsync(ct)).SingleOrDefault();
        if (user is null || session is null) throw InvalidSession();
        var stored = await db.RefreshTokens.SingleAsync(x => x.Id == known.Id, ct);
        var now = clock.GetUtcNow();
        if (stored.ConsumedAt is not null)
        {
            // Retain consumed hashes until the family's expiry so replay revokes the replacement too.
            session.Revoke(now);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            throw InvalidSession();
        }
        if (!stored.CanUse(now) || !session.IsActive(now) || !user.EmailConfirmed
            || session.SecurityStamp != user.SecurityStamp || await users.IsLockedOutAsync(user))
            throw InvalidSession();
        // A Redis outage also prevents issuing fresh tokens. Reads never fall back to allowing access.
        await blacklist.ContainsAsync("availability-check");
        stored.Consume(now);
        var refresh = JwtTokenIssuer.NewRefreshToken();
        db.RefreshTokens.Add(new RefreshToken(session.Id, JwtTokenIssuer.HashRefreshToken(refresh), now, session.ExpiresAt));
        var result = issuer.Issue(await AccountAsync(user), session.Id, refresh, session.ExpiresAt);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task RevokeAsync(AccessTokenContext context, bool allSessions, CancellationToken ct)
    {
        // If Redis fails, keep DB revocation committed so a failed logout cannot leave the session active.
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var user = await LockUserAsync(context.UserId, ct);
            if (user is null) throw InvalidSession();
            var now = clock.GetUtcNow();
            await db.AuthSessions.Where(x => x.UserId == user.Id && x.RevokedAt == null
                    && (allSessions || x.Id == context.SessionId))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
            if (allSessions) IdentityAccounts.Ensure(await users.UpdateSecurityStampAsync(user), "logout_failed");
            await transaction.CommitAsync(ct);
        }
        await blacklist.AddAsync(context.Jti, context.ExpiresAt);
    }

    public async Task<bool> ValidateAsync(AccessTokenContext context, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (context.ExpiresAt <= now || await blacklist.ContainsAsync(context.Jti)) return false;
        return await (from session in db.AuthSessions.AsNoTracking()
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

    private static AuthException InvalidSession() => new(AuthErrorKind.Unauthorized, "invalid_session", "The session is invalid or expired. Please sign in again.");
}
