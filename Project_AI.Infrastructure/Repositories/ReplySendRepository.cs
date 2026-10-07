using Microsoft.EntityFrameworkCore;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Auth;
using Project_AI.Application.DTOs.Replies;
using Project_AI.Application.Interfaces;
using Project_AI.Domain.Entities;
using Project_AI.Domain.Enums;
using Project_AI.Infrastructure.Data;

namespace Project_AI.Infrastructure.Repositories;

public sealed class ReplySendRepository : IReplySendStore
{
    private readonly AuthDbContext _dbContext;
    private readonly ReplyDraftData _data;
    private readonly TimeProvider _timeProvider;
    public ReplySendRepository(AuthDbContext dbContext, ReplyDraftData data, TimeProvider timeProvider)
    {
        _dbContext = dbContext; _data = data; _timeProvider = timeProvider;
    }

    public async Task<ReplySendContext> ReadAsync(Guid userId, Guid mailboxId, Guid draftId, Guid expectedVersion, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        return await ReadLockedAsync(userId, mailboxId, draftId, expectedVersion, cancellationToken);
    }

    public async Task<ReplySendContext> BeginAsync(AccessTokenContext session, Guid mailboxId, Guid draftId,
        Guid expectedVersion, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockSessionAsync(session, cancellationToken);
        var context = await ReadLockedAsync(session.UserId, mailboxId, draftId, expectedVersion, cancellationToken);
        if (context.Draft.Status == ReplyDraftStatus.Sent) return context;
        context.Draft.StartSend(expectedVersion, _timeProvider.GetUtcNow());
        await _data.SaveAsync(context.Draft, expectedVersion, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return context;
    }

    private async Task<ReplySendContext> ReadLockedAsync(Guid userId, Guid mailboxId, Guid draftId,
        Guid expectedVersion, CancellationToken cancellationToken)
    {
        var mailbox = await _data.LockMailboxAsync(userId, mailboxId, cancellationToken);
        var draft = await _data.DraftAsync(mailbox, draftId, cancellationToken);
        var source = await _data.SourceAsync(mailbox, draft.EmailMessageId, cancellationToken);
        // A repeated approval of the same successfully sent version is a read, never a second send.
        if (draft.Status == ReplyDraftStatus.Sent && draft.ApprovedVersion == expectedVersion)
            return new ReplySendContext(source, draft);
        if (draft.Status is ReplyDraftStatus.Sending or ReplyDraftStatus.SendUnknown)
            throw new AppException(ErrorCode.ReplySendUnknown, "Sending is in progress or uncertain. Check Gmail Sent.");
        ReplyDraftData.CheckVersion(draft, expectedVersion);
        if (draft.Status != ReplyDraftStatus.Draft)
            throw new AppException(ErrorCode.DraftNotEditable, "Only ready drafts can be approved.");
        if (ReplyDraftData.IsOutdated(draft, source))
            throw new AppException(ErrorCode.DraftOutdated, "The source email or mailbox changed.");
        return new ReplySendContext(source, draft);
    }

    public async Task<ReplyDraftResponse> CompleteAsync(ReplySendContext context, ReplySendResult result, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var draft = await LockAttemptAsync(context, cancellationToken);
        if (draft is null) throw new AppException(ErrorCode.ReplySendUnknown, "The send record changed after provider acceptance.");
        var originalVersion = draft.Version;
        draft.CompleteSend(result.MessageId, _timeProvider.GetUtcNow());
        await _data.SaveAsync(draft, originalVersion, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return _data.Map(draft, context.Source);
    }

    public async Task FailAsync(ReplySendContext context, bool outcomeUnknown, string errorCode, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var draft = await LockAttemptAsync(context, cancellationToken);
        if (draft is null) return;
        var originalVersion = draft.Version;
        draft.FailSend(outcomeUnknown, errorCode, _timeProvider.GetUtcNow());
        await _data.SaveAsync(draft, originalVersion, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<ReplyDraft?> LockAttemptAsync(ReplySendContext context, CancellationToken cancellationToken)
    {
        // A callback may finish after reconnect/revocation: record its outcome without enabling another send.
        await _dbContext.MailboxConnections.FromSqlInterpolated(
            $"SELECT * FROM \"MailboxConnections\" WHERE \"Id\" = {context.Source.MailboxId} FOR UPDATE")
            .AsNoTracking().ToListAsync(cancellationToken);
        return await _dbContext.ReplyDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == context.Draft.Id
            && x.Status == ReplyDraftStatus.Sending && x.SendAttemptId == context.Draft.SendAttemptId, cancellationToken);
    }

    private async Task LockSessionAsync(AccessTokenContext session, CancellationToken cancellationToken)
    {
        var user = (await _dbContext.Users.FromSqlInterpolated(
            $"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {session.UserId} FOR UPDATE").AsNoTracking()
            .ToListAsync(cancellationToken)).SingleOrDefault();
        var now = _timeProvider.GetUtcNow();
        if (user is null || !user.EmailConfirmed || user.SecurityStamp != session.SecurityStamp
            || session.ExpiresAt <= now || (user.LockoutEnabled && user.LockoutEnd > now)
            || !await _dbContext.AuthSessions.AsNoTracking().AnyAsync(x => x.Id == session.SessionId
                && x.UserId == session.UserId && x.SecurityStamp == session.SecurityStamp
                && x.RevokedAt == null && x.ExpiresAt > now, cancellationToken))
            throw new AppException(ErrorCode.InvalidSession, "The approving session was revoked or expired.");
    }
}
