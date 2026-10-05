using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Mailboxes;
using Project_AI.Application.Interfaces;
using StackExchange.Redis;

namespace Project_AI.Infrastructure.Services;

public sealed class MailboxOAuthRequestStore : IMailboxOAuthRequestStore
{
    private const string KeyPrefix = "inboxagent:mailbox:oauth:";
    private readonly RedisConnection _redisConnection;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _timeProvider;

    public MailboxOAuthRequestStore(RedisConnection redisConnection, IDataProtectionProvider protectionProvider,
        TimeProvider timeProvider)
    {
        _redisConnection = redisConnection;
        _protector = protectionProvider.CreateProtector("InboxAgent.MailboxOAuthState.v1");
        _timeProvider = timeProvider;
    }

    public async Task SaveAsync(string state, MailboxOAuthRequest request, CancellationToken cancellationToken)
    {
        var lifetime = request.ExpiresAt - _timeProvider.GetUtcNow();
        if (lifetime <= TimeSpan.Zero)
        {
            throw new AppException(ErrorCode.InvalidOAuthState, "The authorization request has expired.");
        }
        try
        {
            var connection = await _redisConnection.GetAsync().WaitAsync(cancellationToken);
            var payload = _protector.Protect(JsonSerializer.Serialize(request));
            var saved = await connection.GetDatabase().StringSetAsync(
                Key(state, request.BrowserBindingHash), payload, lifetime, When.NotExists).WaitAsync(cancellationToken);
            if (!saved)
            {
                throw new AppException(ErrorCode.InvalidOAuthState, "Start a new authorization request.");
            }
        }
        catch (RedisException)
        {
            throw Unavailable();
        }
    }

    public async Task<MailboxOAuthRequest?> ConsumeAsync(string state, string browserBindingHash,
        CancellationToken cancellationToken)
    {
        try
        {
            var connection = await _redisConnection.GetAsync().WaitAsync(cancellationToken);
            // Wrong-browser callbacks use a different key and cannot consume the legitimate request.
            // GETDEL consumes the request atomically, including concurrent callbacks.
            var payload = await connection.GetDatabase().StringGetDeleteAsync(Key(state, browserBindingHash))
                .WaitAsync(cancellationToken);
            if (payload.IsNullOrEmpty)
            {
                return null;
            }
            return JsonSerializer.Deserialize<MailboxOAuthRequest>(_protector.Unprotect(payload.ToString()));
        }
        catch (RedisException)
        {
            throw Unavailable();
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            throw new AppException(ErrorCode.InvalidOAuthState, "The authorization request is invalid.");
        }
    }

    private static string Key(string state, string browserBindingHash) => KeyPrefix + state + ":" + browserBindingHash;
    private static AppException Unavailable() => new(ErrorCode.ServiceUnavailable, "The authorization request store is unavailable.");
}
