using Microsoft.EntityFrameworkCore;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.Interfaces;
using Project_AI.Domain.Enums;
using Project_AI.Infrastructure.Data;

namespace Project_AI.Infrastructure.Services;

// Used by provider clients; raw Google tokens never leave Infrastructure through an HTTP endpoint.
public sealed class MailboxAccessTokenService
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(1);
    private readonly AuthDbContext _dbContext;
    private readonly MailboxTokenProtector _tokenProtector;
    private readonly IGoogleOAuthClient _googleClient;
    private readonly TimeProvider _timeProvider;

    public MailboxAccessTokenService(AuthDbContext dbContext, MailboxTokenProtector tokenProtector,
        IGoogleOAuthClient googleClient, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tokenProtector = tokenProtector;
        _googleClient = googleClient;
        _timeProvider = timeProvider;
    }

    public async Task RejectAccessTokenAsync(Guid userId, Guid mailboxId, string rejectedToken, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var mailbox = (await _dbContext.MailboxConnections.FromSqlInterpolated(
            $"SELECT * FROM \"MailboxConnections\" WHERE \"Id\" = {mailboxId} AND \"UserId\" = {userId} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault();
        if (mailbox is null) return;
        await _dbContext.Entry(mailbox).ReloadAsync(cancellationToken);
        if (mailbox.Status != MailboxStatus.Connected) return;
        var credential = await _dbContext.MailboxCredentials.SingleOrDefaultAsync(x => x.MailboxConnectionId == mailboxId, cancellationToken);
        if (credential is not null) await _dbContext.Entry(credential).ReloadAsync(cancellationToken);
        // A late 401 for an old access token must not revoke newly saved authorization.
        if (credential is null || _tokenProtector.Unprotect(mailbox, credential.ProtectedPayload).AccessToken != rejectedToken) return;
        mailbox.RequireReconnect(_timeProvider.GetUtcNow());
        _dbContext.MailboxCredentials.Remove(credential);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<string> GetAccessTokenAsync(Guid userId, Guid mailboxId, CancellationToken cancellationToken,
        bool forceRefresh = false, bool requireSendPermission = false, Guid? expectedMailboxVersion = null)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        // Serialize refresh with reconnect/disconnect across API instances; the provider request has a timeout.
        var mailbox = (await _dbContext.MailboxConnections.FromSqlInterpolated(
            $"SELECT * FROM \"MailboxConnections\" WHERE \"Id\" = {mailboxId} AND \"UserId\" = {userId} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault();
        if (mailbox is null)
        {
            throw new AppException(ErrorCode.NotFound, "The mailbox was not found.");
        }
        await _dbContext.Entry(mailbox).ReloadAsync(cancellationToken);
        if (expectedMailboxVersion is Guid expected && mailbox.Version != expected)
            throw new AppException(ErrorCode.DraftOutdated, "The mailbox changed before sending.");
        if (mailbox.Status != MailboxStatus.Connected)
        {
            throw new AppException(ErrorCode.MailboxReconnectRequired, "Reconnect the mailbox first.");
        }

        var credential = await _dbContext.MailboxCredentials
            .SingleOrDefaultAsync(x => x.MailboxConnectionId == mailbox.Id, cancellationToken);
        if (credential is not null) await _dbContext.Entry(credential).ReloadAsync(cancellationToken);
        try
        {
            if (credential is null)
            {
                throw new AppException(ErrorCode.MailboxReconnectRequired, "The mailbox credentials are missing.");
            }
            var tokens = _tokenProtector.Unprotect(mailbox, credential.ProtectedPayload);
            var now = _timeProvider.GetUtcNow();
            if (string.IsNullOrWhiteSpace(tokens.RefreshToken) || tokens.RefreshTokenExpiresAt <= now)
            {
                throw new AppException(ErrorCode.MailboxReconnectRequired, "Google offline authorization has expired.");
            }
            if (!forceRefresh && tokens.AccessTokenExpiresAt > now + RefreshMargin)
            {
                CheckSendPermission(tokens.Scope, requireSendPermission);
                return tokens.AccessToken;
            }

            var refreshed = await _googleClient.RefreshAsync(tokens.RefreshToken, cancellationToken);
            var scope = string.IsNullOrWhiteSpace(refreshed.Scope) ? tokens.Scope : refreshed.Scope;
            if (!scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(GoogleOAuthClient.ReadOnlyScope))
            {
                throw new AppException(ErrorCode.MailboxReconnectRequired, "Gmail read permission is no longer granted.");
            }
            var keepsRefreshToken = refreshed.RefreshToken is null || refreshed.RefreshToken == tokens.RefreshToken;
            refreshed = refreshed with
            {
                RefreshToken = refreshed.RefreshToken ?? tokens.RefreshToken,
                RefreshTokenExpiresAt = refreshed.RefreshTokenExpiresAt
                    ?? (keepsRefreshToken ? tokens.RefreshTokenExpiresAt : null),
                Scope = scope
            };
            credential.ProtectedPayload = _tokenProtector.Protect(mailbox, refreshed);
            credential.AccessTokenExpiresAt = refreshed.AccessTokenExpiresAt;
            credential.RefreshTokenExpiresAt = refreshed.RefreshTokenExpiresAt;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            CheckSendPermission(refreshed.Scope, requireSendPermission);
            return refreshed.AccessToken;
        }
        catch (AppException exception) when (exception.Code == ErrorCode.MailboxReconnectRequired)
        {
            mailbox.RequireReconnect(_timeProvider.GetUtcNow());
            if (credential is not null)
            {
                _dbContext.MailboxCredentials.Remove(credential);
            }
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw;
        }
    }

    private static void CheckSendPermission(string scope, bool required)
    {
        if (required && !scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(GoogleOAuthClient.SendScope))
            throw new AppException(ErrorCode.MailboxSendPermissionRequired, "Grant gmail.send before sending replies.");
    }
}
