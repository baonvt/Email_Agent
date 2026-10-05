using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Auth;
using Project_AI.Application.DTOs.Mailboxes;
using Project_AI.Application.Interfaces;
using Project_AI.Domain.Entities;

namespace Project_AI.Application.Services;

public sealed class MailboxConnectionService : IMailboxConnectionService
{
    private static readonly TimeSpan AuthorizationLifetime = TimeSpan.FromMinutes(10);
    private readonly IGoogleOAuthClient _googleClient;
    private readonly IMailboxConnectionStore _connections;
    private readonly IMailboxOAuthRequestStore _requests;
    private readonly IAuthSessionService _sessions;
    private readonly TimeProvider _timeProvider;

    public MailboxConnectionService(IGoogleOAuthClient googleClient, IMailboxConnectionStore connections,
        IMailboxOAuthRequestStore requests, IAuthSessionService sessions, TimeProvider timeProvider)
    {
        _googleClient = googleClient;
        _connections = connections;
        _requests = requests;
        _sessions = sessions;
        _timeProvider = timeProvider;
    }

    public async Task<MailboxAuthorization> BeginAsync(AccessTokenContext session, string browserBinding,
        CancellationToken cancellationToken)
    {
        if (!IsOpaqueValue(browserBinding))
        {
            throw InvalidState();
        }
        await RequireSessionAsync(session, cancellationToken);
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var url = _googleClient.CreateAuthorizationUrl(state, challenge);
        var mailbox = await _connections.GetOrCreateAsync(session, cancellationToken);
        var expiresAt = _timeProvider.GetUtcNow() + AuthorizationLifetime;
        if (session.ExpiresAt < expiresAt)
        {
            expiresAt = session.ExpiresAt;
        }
        var request = new MailboxOAuthRequest(session, mailbox.Id, mailbox.Version,
            HashBinding(browserBinding), verifier, expiresAt);
        await _requests.SaveAsync(state, request, cancellationToken);
        return new MailboxAuthorization(url, expiresAt);
    }

    public async Task<MailboxResponse> CompleteAsync(string state, string? code, string? error, string browserBinding,
        CancellationToken cancellationToken)
    {
        if (!IsOpaqueValue(state) || !IsOpaqueValue(browserBinding))
        {
            throw InvalidState();
        }
        var request = await _requests.ConsumeAsync(state, HashBinding(browserBinding), cancellationToken);
        if (request is null || request.ExpiresAt <= _timeProvider.GetUtcNow()
            || request.BrowserBindingHash != HashBinding(browserBinding))
        {
            throw InvalidState();
        }
        await RequireSessionAsync(request.Session, cancellationToken);
        if (!string.IsNullOrEmpty(error))
        {
            throw new AppException(ErrorCode.OAuthDenied, "Google mailbox authorization was declined.");
        }
        if (string.IsNullOrWhiteSpace(code) || code.Length > 4096)
        {
            throw new AppException(ErrorCode.OAuthRejected, "The Google authorization code is missing or invalid.");
        }

        var tokens = await _googleClient.ExchangeCodeAsync(code, request.CodeVerifier, cancellationToken);
        var account = await _googleClient.GetMailboxAccountAsync(tokens.AccessToken, cancellationToken);
        var mailbox = await _connections.ConnectAsync(request.Session, request.MailboxId, request.Version,
            account, tokens, cancellationToken);
        return ToResponse(mailbox);
    }

    public async Task<IReadOnlyList<MailboxResponse>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        (await _connections.ListAsync(userId, cancellationToken)).Select(ToResponse).ToArray();

    public async Task DisconnectAsync(AccessTokenContext session, CancellationToken cancellationToken)
    {
        await RequireSessionAsync(session, cancellationToken);
        await _connections.DisconnectAsync(session, cancellationToken);
    }

    private async Task RequireSessionAsync(AccessTokenContext session, CancellationToken cancellationToken)
    {
        if (!await _sessions.ValidateAsync(session, cancellationToken))
        {
            throw new AppException(ErrorCode.InvalidSession, "Sign in to InboxAgent again before connecting a mailbox.");
        }
    }

    private static MailboxResponse ToResponse(MailboxConnection mailbox) => new(mailbox.Id,
        mailbox.Provider.ToString().ToLowerInvariant(), mailbox.Email,
        JsonNamingPolicy.SnakeCaseLower.ConvertName(mailbox.Status.ToString()), mailbox.ConnectedAt, mailbox.UpdatedAt);
    private static bool IsOpaqueValue(string value) => value.Length == 43
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    private static string HashBinding(string value) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(value)));
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static AppException InvalidState() => new(ErrorCode.InvalidOAuthState,
        "The authorization request is invalid or expired. Start again in the same browser.");
}
