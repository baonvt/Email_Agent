using Microsoft.EntityFrameworkCore;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Auth;
using Project_AI.Application.DTOs.Mailboxes;
using Project_AI.Application.Interfaces;
using Project_AI.Domain.Entities;
using Project_AI.Domain.Enums;
using Project_AI.Infrastructure.Data;
using Project_AI.Infrastructure.Data.Entities;
using Project_AI.Infrastructure.Services;

namespace Project_AI.Infrastructure.Repositories;

public sealed class MailboxConnectionRepository : IMailboxConnectionStore
{
    private readonly AuthDbContext _dbContext;
    private readonly MailboxTokenProtector _tokenProtector;
    private readonly TimeProvider _timeProvider;

    public MailboxConnectionRepository(AuthDbContext dbContext, MailboxTokenProtector tokenProtector,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tokenProtector = tokenProtector;
        _timeProvider = timeProvider;
    }

    public async Task<MailboxConnection> GetOrCreateAsync(AccessTokenContext session, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockActiveUserAsync(session, cancellationToken);
        var mailbox = await _dbContext.MailboxConnections
            .SingleOrDefaultAsync(x => x.UserId == session.UserId && x.Provider == MailboxProvider.Gmail, cancellationToken);
        if (mailbox is null)
        {
            mailbox = new MailboxConnection(session.UserId, MailboxProvider.Gmail, _timeProvider.GetUtcNow());
            _dbContext.MailboxConnections.Add(mailbox);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return mailbox;
    }

    public async Task<IReadOnlyList<MailboxConnection>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        await _dbContext.MailboxConnections.AsNoTracking().Where(x => x.UserId == userId)
            .OrderBy(x => x.CreatedAt).ToListAsync(cancellationToken);

    public async Task<MailboxConnection> ConnectAsync(AccessTokenContext session, Guid mailboxId, Guid expectedVersion,
        GoogleMailboxAccount account, GoogleTokenSet tokens, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockActiveUserAsync(session, cancellationToken);
        var mailbox = await LockMailboxAsync(session.UserId, mailboxId, cancellationToken);
        if (mailbox is null || mailbox.Version != expectedVersion)
        {
            throw new AppException(ErrorCode.Conflict, "The mailbox connection changed. Start authorization again.");
        }
        if (mailbox.Status != MailboxStatus.Disconnected && mailbox.ProviderAccountId != account.AccountId)
        {
            throw new AppException(ErrorCode.Conflict, "Disconnect the existing Gmail account before connecting another.");
        }

        var credential = await _dbContext.MailboxCredentials
            .SingleOrDefaultAsync(x => x.MailboxConnectionId == mailbox.Id, cancellationToken);
        if (string.IsNullOrWhiteSpace(tokens.RefreshToken))
        {
            // Re-consent can omit the refresh token; retain it only for the same connected Google account.
            if (credential is null || mailbox.ProviderAccountId != account.AccountId
                || mailbox.Status == MailboxStatus.Disconnected)
            {
                throw new AppException(ErrorCode.OAuthRejected, "Google did not grant offline access. Authorize again.");
            }
            var previous = _tokenProtector.Unprotect(mailbox, credential.ProtectedPayload);
            if (string.IsNullOrWhiteSpace(previous.RefreshToken)
                || previous.RefreshTokenExpiresAt <= _timeProvider.GetUtcNow())
            {
                throw new AppException(ErrorCode.MailboxReconnectRequired, "Authorize the mailbox again for offline access.");
            }
            tokens = tokens with
            {
                RefreshToken = previous.RefreshToken,
                RefreshTokenExpiresAt = previous.RefreshTokenExpiresAt
            };
        }

        if (mailbox.ProviderAccountId != account.AccountId)
            await ClearEmailDataAsync(mailbox.Id, cancellationToken);
        else
            await _dbContext.MailboxSyncStates.Where(x => x.MailboxConnectionId == mailbox.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.LeaseId, (Guid?)null)
                    .SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null), cancellationToken);
        mailbox.Connect(account.AccountId, account.Email, _timeProvider.GetUtcNow());
        if (credential is null)
        {
            credential = new MailboxCredential { MailboxConnectionId = mailbox.Id, ProtectedPayload = "" };
            _dbContext.MailboxCredentials.Add(credential);
        }
        credential.ProtectedPayload = _tokenProtector.Protect(mailbox, tokens);
        credential.AccessTokenExpiresAt = tokens.AccessTokenExpiresAt;
        credential.RefreshTokenExpiresAt = tokens.RefreshTokenExpiresAt;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return mailbox;
    }

    public async Task DisconnectAsync(AccessTokenContext session, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockActiveUserAsync(session, cancellationToken);
        var mailbox = (await _dbContext.MailboxConnections.FromSqlInterpolated(
            $"SELECT * FROM \"MailboxConnections\" WHERE \"UserId\" = {session.UserId} AND \"Provider\" = {MailboxProvider.Gmail.ToString()} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault();
        if (mailbox is not null)
        {
            mailbox.Disconnect(_timeProvider.GetUtcNow());
            await ClearEmailDataAsync(mailbox.Id, cancellationToken);
            var credential = await _dbContext.MailboxCredentials
                .SingleOrDefaultAsync(x => x.MailboxConnectionId == mailbox.Id, cancellationToken);
            if (credential is not null)
            {
                _dbContext.MailboxCredentials.Remove(credential);
            }
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ClearEmailDataAsync(Guid mailboxId, CancellationToken cancellationToken)
    {
        // Retain send receipts/uncertain attempts across disconnect, while removing unsent content.
        await _dbContext.ReplyDrafts.Where(x => x.MailboxConnectionId == mailboxId
            && (x.Status == ReplyDraftStatus.Draft || x.Status == ReplyDraftStatus.Generating
                || x.Status == ReplyDraftStatus.GenerationFailed)).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.EmailMessages.Where(x => x.MailboxConnectionId == mailboxId).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.MailboxSyncStates.Where(x => x.MailboxConnectionId == mailboxId).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<MailboxConnection?> LockMailboxAsync(Guid userId, Guid mailboxId, CancellationToken cancellationToken) =>
        (await _dbContext.MailboxConnections.FromSqlInterpolated(
            $"SELECT * FROM \"MailboxConnections\" WHERE \"Id\" = {mailboxId} AND \"UserId\" = {userId} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault();

    private async Task LockActiveUserAsync(AccessTokenContext session, CancellationToken cancellationToken)
    {
        // Use the same user-first lock order as logout/reset, so a revoked session cannot finish OAuth.
        var user = (await _dbContext.Users.FromSqlInterpolated(
            $"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {session.UserId} FOR UPDATE").AsNoTracking()
            .ToListAsync(cancellationToken)).SingleOrDefault();
        var now = _timeProvider.GetUtcNow();
        if (user is null || !user.EmailConfirmed || user.SecurityStamp != session.SecurityStamp
            || session.ExpiresAt <= now || (user.LockoutEnabled && user.LockoutEnd > now)
            || !await _dbContext.AuthSessions.AsNoTracking().AnyAsync(x => x.Id == session.SessionId
                && x.UserId == session.UserId && x.SecurityStamp == session.SecurityStamp
                && x.RevokedAt == null && x.ExpiresAt > now, cancellationToken))
        {
            throw new AppException(ErrorCode.InvalidSession, "The InboxAgent session is no longer valid.");
        }
    }
}
