using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using StackExchange.Redis;

namespace Project_AI.Infrastructure.Services;

public sealed class TokenBlacklist
{
    private const string Prefix = "inboxagent:auth:revoked:";

    private readonly RedisConnection _redisConnection;
    private readonly TimeProvider _timeProvider;

    public TokenBlacklist(RedisConnection redisConnection, TimeProvider timeProvider)
    {
        _redisConnection = redisConnection;
        _timeProvider = timeProvider;
    }

    public async Task<bool> ContainsAsync(string jti)
    {
        try
        {
            var connection = await _redisConnection.GetAsync();
            return await connection.GetDatabase().KeyExistsAsync(Prefix + jti);
        }
        catch (RedisException)
        {
            throw Unavailable();
        }
    }

    public async Task AddAsync(string jti, DateTimeOffset expiresAt)
    {
        var ttl = expiresAt - _timeProvider.GetUtcNow();
        if (ttl <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            var connection = await _redisConnection.GetAsync();
            await connection.GetDatabase().StringSetAsync(Prefix + jti, "1", ttl);
        }
        catch (RedisException)
        {
            throw Unavailable();
        }
    }

    private static AppException Unavailable() => new(ErrorCode.AuthUnavailable,
        "Authentication is temporarily unavailable. Please try again later.");
}
