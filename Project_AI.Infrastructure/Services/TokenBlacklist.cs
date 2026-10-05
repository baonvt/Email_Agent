using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using StackExchange.Redis;

namespace Project_AI.Infrastructure.Services;

public sealed class TokenBlacklist(RedisConnection connection, TimeProvider clock)
{
    private const string Prefix = "inboxagent:auth:revoked:";

    public async Task<bool> ContainsAsync(string jti)
    {
        try { return await (await connection.GetAsync()).GetDatabase().KeyExistsAsync(Prefix + jti); }
        catch (RedisException) { throw Unavailable(); }
    }

    public async Task AddAsync(string jti, DateTimeOffset expiresAt)
    {
        var ttl = expiresAt - clock.GetUtcNow();
        if (ttl <= TimeSpan.Zero) return;
        try { await (await connection.GetAsync()).GetDatabase().StringSetAsync(Prefix + jti, "1", ttl); }
        catch (RedisException) { throw Unavailable(); }
    }

    private static AppException Unavailable() => new(ErrorCode.AuthUnavailable,
        "Authentication is temporarily unavailable. Please try again later.");
}
