using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Emails;
using Project_AI.Application.DTOs.Replies;
using Project_AI.Domain.Entities;
using Project_AI.Domain.Enums;
using Project_AI.Infrastructure.Data;

namespace Project_AI.Infrastructure.Repositories;

// Mailbox-first locking matches synchronization, reconnect and disconnect.
public sealed class ReplyDraftData
{
    private readonly AuthDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    public ReplyDraftData(AuthDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext; _timeProvider = timeProvider;
    }

    public async Task<MailboxConnection> LockMailboxAsync(Guid userId, Guid mailboxId, CancellationToken cancellationToken)
    {
        var mailbox = (await _dbContext.MailboxConnections.FromSqlInterpolated(
            $"SELECT * FROM \"MailboxConnections\" WHERE \"Id\" = {mailboxId} AND \"UserId\" = {userId} FOR UPDATE")
            .AsNoTracking().ToListAsync(cancellationToken)).SingleOrDefault()
            ?? throw new AppException(ErrorCode.NotFound, "The mailbox was not found.");
        if (mailbox.Status != MailboxStatus.Connected)
            throw new AppException(ErrorCode.MailboxReconnectRequired, "Reconnect the mailbox first.");
        return mailbox;
    }

    public async Task<ReplySource> SourceAsync(MailboxConnection mailbox, Guid emailId, CancellationToken cancellationToken)
    {
        var email = await _dbContext.EmailMessages.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == emailId && x.MailboxConnectionId == mailbox.Id, cancellationToken)
            ?? throw new AppException(ErrorCode.NotFound, "The email was not found.");
        return new ReplySource(mailbox.UserId, mailbox.Id, mailbox.Version, email.ContentVersion, mailbox.Email!,
            new ProviderEmail(email.ProviderMessageId, email.ThreadId, email.Subject, email.From, email.To, email.Snippet,
                email.BodyText, email.BodyTruncated, email.HasAttachments, email.Labels, email.ReceivedAt));
    }

    public async Task<ReplyDraft> DraftAsync(MailboxConnection mailbox, Guid draftId, CancellationToken cancellationToken) =>
        await (from draft in _dbContext.ReplyDrafts.AsNoTracking()
               join email in _dbContext.EmailMessages on draft.EmailMessageId equals email.Id
               where draft.Id == draftId && email.MailboxConnectionId == mailbox.Id
               select draft).SingleOrDefaultAsync(cancellationToken)
        ?? throw new AppException(ErrorCode.NotFound, "The draft was not found.");

    public ReplyDraftResponse Map(ReplyDraft draft, ReplySource source)
    {
        var status = draft.Status == ReplyDraftStatus.Sending && draft.SendStartedAt <= _timeProvider.GetUtcNow().AddMinutes(-2)
            ? "send_unknown" : JsonNamingPolicy.SnakeCaseLower.ConvertName(draft.Status.ToString());
        return new ReplyDraftResponse(draft.Id, draft.EmailMessageId, draft.Version, status, draft.From, draft.To,
            draft.Subject, draft.BodyText, IsOutdated(draft, source), draft.InputTruncated, draft.Model, draft.ProviderMessageId,
            draft.OutgoingMessageId, draft.CreatedAt, draft.UpdatedAt, draft.SentAt, draft.LastErrorCode);
    }

    public static bool IsOutdated(ReplyDraft draft, ReplySource source) =>
        draft.SourceVersion != source.SourceVersion || draft.MailboxVersion != source.MailboxVersion;
    public static void CheckVersion(ReplyDraft draft, Guid version)
    {
        if (version == Guid.Empty || draft.Version != version)
            throw new AppException(ErrorCode.DraftOutdated, "The draft changed. Reload it before continuing.");
    }
    public async Task SaveAsync(ReplyDraft draft, Guid originalVersion, CancellationToken cancellationToken)
    {
        _dbContext.Update(draft);
        // The detached entity has already changed Version; EF must compare against the version read from DB.
        _dbContext.Entry(draft).Property(x => x.Version).OriginalValue = originalVersion;
        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.Entry(draft).State = EntityState.Detached;
    }
}
