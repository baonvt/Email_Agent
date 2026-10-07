using Microsoft.EntityFrameworkCore;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Replies;
using Project_AI.Application.Interfaces;
using Project_AI.Domain.Entities;
using Project_AI.Domain.Enums;
using Project_AI.Infrastructure.Data;

namespace Project_AI.Infrastructure.Repositories;

public sealed class ReplyDraftRepository : IReplyDraftStore
{
    private readonly AuthDbContext _dbContext;
    private readonly ReplyDraftData _data;
    private readonly TimeProvider _timeProvider;
    public ReplyDraftRepository(AuthDbContext dbContext, ReplyDraftData data, TimeProvider timeProvider)
    {
        _dbContext = dbContext; _data = data; _timeProvider = timeProvider;
    }

    public async Task<ReplyGenerationContext> BeginGenerationAsync(Guid userId, Guid mailboxId, Guid emailId,
        bool force, Guid? expectedVersion, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var mailbox = await _data.LockMailboxAsync(userId, mailboxId, cancellationToken);
        var source = await _data.SourceAsync(mailbox, emailId, cancellationToken);
        var draft = await _dbContext.ReplyDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.EmailMessageId == emailId, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        if (draft is not null)
        {
            if (draft.Status is ReplyDraftStatus.Sending or ReplyDraftStatus.Sent or ReplyDraftStatus.SendUnknown)
                throw NotEditable();
            if (draft.Status == ReplyDraftStatus.Generating && draft.GenerationExpiresAt > now)
                throw new AppException(ErrorCode.DraftInProgress, "Draft generation is already running.");
            if (!force && draft.Status == ReplyDraftStatus.Draft && !ReplyDraftData.IsOutdated(draft, source))
                return new ReplyGenerationContext(draft.Id, Guid.Empty, source, _data.Map(draft, source));
            if (draft.BodyText.Length > 0 || force)
                ReplyDraftData.CheckVersion(draft, expectedVersion ?? Guid.Empty);
            if (!force && draft.BodyText.Length > 0)
                throw new AppException(ErrorCode.DraftOutdated, "Explicitly regenerate the outdated draft after reviewing its version.");
        }
        else
        {
            if (force) throw new AppException(ErrorCode.InvalidInput, "There is no draft to regenerate.");
            draft = new ReplyDraft(emailId, now);
            _dbContext.ReplyDrafts.Add(draft);
        }
        var originalVersion = draft.Version;
        draft.StartGeneration(now);
        if (_dbContext.Entry(draft).State != EntityState.Added)
        {
            _dbContext.Update(draft);
            _dbContext.Entry(draft).Property(x => x.Version).OriginalValue = originalVersion;
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _dbContext.Entry(draft).State = EntityState.Detached;
        return new ReplyGenerationContext(draft.Id, draft.GenerationId!.Value, source, null);
    }

    public async Task<ReplyDraftResponse> CompleteGenerationAsync(ReplyGenerationContext context, ReplyThread thread,
        GeneratedReply output, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var mailbox = await _data.LockMailboxAsync(context.Source.UserId, context.Source.MailboxId, cancellationToken);
        var draft = await _data.DraftAsync(mailbox, context.DraftId, cancellationToken);
        var source = await _data.SourceAsync(mailbox, draft.EmailMessageId, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        if (draft.Status != ReplyDraftStatus.Generating || draft.GenerationId != context.GenerationId
            || draft.GenerationExpiresAt is null || draft.GenerationExpiresAt <= now
            || source.SourceVersion != context.Source.SourceVersion || source.MailboxVersion != context.Source.MailboxVersion)
            throw new AppException(ErrorCode.DraftOutdated, "The email, mailbox or generation request changed.");
        var originalVersion = draft.Version;
        draft.CompleteGeneration(source.SourceVersion, source.MailboxVersion, source.MailboxEmail, thread.To,
            source.Email.Subject, output.BodyText, thread.InReplyTo, thread.References, source.Email.ThreadId,
            thread.Fingerprint, output.InputTruncated || thread.Truncated, output.Model, now);
        await _data.SaveAsync(draft, originalVersion, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return _data.Map(draft, source);
    }

    public async Task FailGenerationAsync(ReplyGenerationContext context, string errorCode, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        // Cleanup must also work after authorization becomes requires_reconnect.
        await _dbContext.MailboxConnections.FromSqlInterpolated(
            $"SELECT * FROM \"MailboxConnections\" WHERE \"Id\" = {context.Source.MailboxId} FOR UPDATE")
            .AsNoTracking().ToListAsync(cancellationToken);
        var draft = await _dbContext.ReplyDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == context.DraftId, cancellationToken);
        if (draft?.Status != ReplyDraftStatus.Generating || draft.GenerationId != context.GenerationId) return;
        var originalVersion = draft.Version;
        draft.FailGeneration(errorCode, _timeProvider.GetUtcNow());
        await _data.SaveAsync(draft, originalVersion, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ReplyDraftResponse> GetAsync(Guid userId, Guid mailboxId, Guid draftId, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var mailbox = await _data.LockMailboxAsync(userId, mailboxId, cancellationToken);
        var draft = await _data.DraftAsync(mailbox, draftId, cancellationToken);
        return _data.Map(draft, await _data.SourceAsync(mailbox, draft.EmailMessageId, cancellationToken));
    }

    public async Task<IReadOnlyList<ReplyDraftResponse>> ListAsync(Guid userId, Guid mailboxId, Guid emailId, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var mailbox = await _data.LockMailboxAsync(userId, mailboxId, cancellationToken);
        var source = await _data.SourceAsync(mailbox, emailId, cancellationToken);
        var draft = await _dbContext.ReplyDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.EmailMessageId == emailId, cancellationToken);
        return draft is null ? [] : [_data.Map(draft, source)];
    }

    public async Task<ReplyDraftResponse> EditAsync(Guid userId, Guid mailboxId, Guid draftId, Guid expectedVersion,
        string body, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var mailbox = await _data.LockMailboxAsync(userId, mailboxId, cancellationToken);
        var draft = await _data.DraftAsync(mailbox, draftId, cancellationToken);
        ReplyDraftData.CheckVersion(draft, expectedVersion);
        if (draft.Status != ReplyDraftStatus.Draft) throw NotEditable();
        if (string.IsNullOrWhiteSpace(body) || body.Length > 10000 || body.Contains('\0'))
            throw new AppException(ErrorCode.InvalidInput, "The reply body is invalid.");
        draft.Edit(body, _timeProvider.GetUtcNow());
        await _data.SaveAsync(draft, expectedVersion, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return _data.Map(draft, await _data.SourceAsync(mailbox, draft.EmailMessageId, cancellationToken));
    }

    public async Task DeleteAsync(Guid userId, Guid mailboxId, Guid draftId, Guid expectedVersion, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var mailbox = await _data.LockMailboxAsync(userId, mailboxId, cancellationToken);
        var draft = await _data.DraftAsync(mailbox, draftId, cancellationToken);
        ReplyDraftData.CheckVersion(draft, expectedVersion);
        if (draft.Status is not (ReplyDraftStatus.Draft or ReplyDraftStatus.GenerationFailed)) throw NotEditable();
        await _dbContext.ReplyDrafts.Where(x => x.Id == draft.Id).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static AppException NotEditable() => new(ErrorCode.DraftNotEditable, "The draft is locked in its current state.");
}
