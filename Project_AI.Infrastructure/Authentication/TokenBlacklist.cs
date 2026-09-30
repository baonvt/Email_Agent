
using StackExchange.Redis;

namespace Project_AI.Infrastructure.Authentication;

public sealed class TokenBlacklist(IConnectionMultiplexer redis, TimeProvider clock)
{
    private const string Prefix = "inboxagent:auth:revoked:";

    public async Task<bool> ContainsAsync(string jti)
    {
        try { return await redis.GetDatabase().KeyExistsAsync(Prefix + jti); }
        catch (RedisException) { throw Unavailable(); }
    }

    public async Task AddAsync(string jti, DateTimeOffset expiresAt)
    {
        var ttl = expiresAt - clock.GetUtcNow();
        if (ttl <= TimeSpan.Zero) return;
        try { await redis.GetDatabase().StringSetAsync(Prefix + jti, "1", ttl); }
        catch (RedisException) { throw Unavailable(); }
    }

    private static AuthException Unavailable() => new(AuthErrorKind.Unavailable, "auth_unavailable",
        "Authentication is temporarily unavailable. Please try again later.");
}
