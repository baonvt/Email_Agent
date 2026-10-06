using Microsoft.EntityFrameworkCore;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Emails;
using Project_AI.Application.Interfaces;
using Project_AI.Domain.Entities;
using Project_AI.Domain.Enums;
using Project_AI.Infrastructure.Data;
using Project_AI.Infrastructure.Data.Entities;

namespace Project_AI.Infrastructure.Repositories;

public sealed class EmailSyncRepository : IEmailSyncStore
{
    private readonly AuthDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public EmailSyncRepository(AuthDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<MailboxSyncContext> BeginAsync(Guid userId, Guid mailboxId, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var mailbox = await LockMailboxAsync(userId, mailboxId, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var state = await _dbContext.MailboxSyncStates.SingleOrDefaultAsync(x => x.MailboxConnectionId == mailboxId, cancellationToken);
        if (state?.LeaseExpiresAt > now)
            throw new AppException(ErrorCode.MailboxSyncInProgress, "A synchronization is already running.");
        if (state is null)
        {
            state = new MailboxSyncState { MailboxConnectionId = mailboxId };
            _dbContext.MailboxSyncStates.Add(state);
        }
        state.LeaseId = Guid.NewGuid();
        state.LeaseExpiresAt = now.AddMinutes(5);
        state.LastAttemptAt = now;
        state.LastErrorCode = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var result = new MailboxSyncContext(userId, mailboxId, mailbox.Version, state.LeaseId.Value, state.HistoryId);
        _dbContext.Entry(state).State = EntityState.Detached; // Re-read the lease after provider I/O.
        return result;
    }

    public async Task<MailboxSyncResult> CompleteAsync(MailboxSyncContext context, IReadOnlyList<ProviderEmail> messages,
        IReadOnlyList<string> removedIds, string historyId, bool replaceSnapshot, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var mailbox = await LockMailboxAsync(context.UserId, context.MailboxId, cancellationToken);
        var state = await _dbContext.MailboxSyncStates.SingleOrDefaultAsync(x => x.MailboxConnectionId == context.MailboxId, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        if (mailbox.Version != context.MailboxVersion || state is null || state.LeaseId != context.LeaseId
            || state.LeaseExpiresAt <= now)
            throw new AppException(ErrorCode.Conflict, "The mailbox or synchronization lease changed. Start again.");

        var incoming = messages.DistinctBy(x => x.MessageId).ToArray();
        var ids = incoming.Select(x => x.MessageId).ToArray();
        var rows = _dbContext.EmailMessages.Where(x => x.MailboxConnectionId == context.MailboxId);
        var removed = replaceSnapshot
            ? await rows.Where(x => !ids.Contains(x.ProviderMessageId)).ExecuteDeleteAsync(cancellationToken)
            : await rows.Where(x => removedIds.Contains(x.ProviderMessageId)).ExecuteDeleteAsync(cancellationToken);
        var existing = await rows.Where(x => ids.Contains(x.ProviderMessageId))
            .ToDictionaryAsync(x => x.ProviderMessageId, cancellationToken);
        foreach (var message in incoming)
        {
            if (!existing.TryGetValue(message.MessageId, out var entity))
            {
                entity = new EmailMessage(context.MailboxId, message.MessageId);
                _dbContext.EmailMessages.Add(entity);
            }
            entity.Update(message.ThreadId, message.Subject, message.From, message.To, message.Snippet,
                message.BodyText, message.BodyTruncated, message.HasAttachments, message.Labels, message.ReceivedAt, now);
        }
        state.HistoryId = historyId;
        state.LastSyncedAt = now;
        state.LastErrorCode = null;
        state.LeaseId = null;
        state.LeaseExpiresAt = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new MailboxSyncResult(context.MailboxId, replaceSnapshot ? "snapshot" : "incremental", incoming.Length, removed, now);
    }

    public async Task FailAsync(MailboxSyncContext context, string errorCode, CancellationToken cancellationToken)
    {
        // A failed/late worker cannot clear a newer worker's lease or advance its cursor.
        await _dbContext.MailboxSyncStates.Where(x => x.MailboxConnectionId == context.MailboxId && x.LeaseId == context.LeaseId)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.LastErrorCode, errorCode)
                .SetProperty(x => x.LeaseId, (Guid?)null).SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null), cancellationToken);
    }

    public async Task<MailboxSyncStatus> GetStatusAsync(Guid userId, Guid mailboxId, CancellationToken cancellationToken)
    {
        if (!await _dbContext.MailboxConnections.AnyAsync(x => x.Id == mailboxId && x.UserId == userId, cancellationToken))
            throw new AppException(ErrorCode.NotFound, "The mailbox was not found.");
        var state = await _dbContext.MailboxSyncStates.AsNoTracking().SingleOrDefaultAsync(x => x.MailboxConnectionId == mailboxId, cancellationToken);
        return new MailboxSyncStatus(mailboxId, state?.LastAttemptAt, state?.LastSyncedAt,
            state?.LastErrorCode, state?.LeaseExpiresAt > _timeProvider.GetUtcNow());
    }

    public async Task<IReadOnlyList<MailboxSyncTarget>> ListDueAsync(DateTimeOffset attemptedBefore, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        return await (from mailbox in _dbContext.MailboxConnections.AsNoTracking()
                      join state in _dbContext.MailboxSyncStates on mailbox.Id equals state.MailboxConnectionId into states
                      from state in states.DefaultIfEmpty()
                      where mailbox.Status == MailboxStatus.Connected && mailbox.Provider == MailboxProvider.Gmail
                          && (state == null || state.LastAttemptAt <= attemptedBefore || state.LastAttemptAt == null)
                          && (state == null || state.LeaseExpiresAt == null || state.LeaseExpiresAt <= now)
                      orderby state.LastAttemptAt, mailbox.Id
                      select new MailboxSyncTarget(mailbox.UserId, mailbox.Id)).Take(10).ToListAsync(cancellationToken);
    }

    private async Task<MailboxConnection> LockMailboxAsync(Guid userId, Guid mailboxId, CancellationToken cancellationToken)
    {
        var mailbox = (await _dbContext.MailboxConnections.FromSqlInterpolated(
            $"SELECT * FROM \"MailboxConnections\" WHERE \"Id\" = {mailboxId} AND \"UserId\" = {userId} FOR UPDATE")
            .AsNoTracking().ToListAsync(cancellationToken)).SingleOrDefault()
            ?? throw new AppException(ErrorCode.NotFound, "The mailbox was not found.");
        if (mailbox.Status != MailboxStatus.Connected)
            throw new AppException(ErrorCode.MailboxReconnectRequired, "Reconnect the mailbox before synchronizing.");
        return mailbox;
    }
}
