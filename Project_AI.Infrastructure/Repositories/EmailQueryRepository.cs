using Microsoft.EntityFrameworkCore;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Emails;
using Project_AI.Application.Interfaces;
using Project_AI.Domain.Enums;
using Project_AI.Infrastructure.Data;

namespace Project_AI.Infrastructure.Repositories;

public sealed class EmailQueryRepository : IEmailQueryStore
{
    private readonly AuthDbContext _dbContext;

    public EmailQueryRepository(AuthDbContext dbContext) { _dbContext = dbContext; }

    public async Task<EmailPage> ListAsync(Guid userId, Guid mailboxId, int page, int pageSize,
        bool unreadOnly, CancellationToken cancellationToken)
    {
        if (page is < 1 or > 100_000 || pageSize is < 1 or > 100)
            throw new AppException(ErrorCode.InvalidInput, "Use page 1–100000 and pageSize 1–100.");
        await RequireMailboxAsync(userId, mailboxId, cancellationToken);
        var query = _dbContext.EmailMessages.AsNoTracking().Where(x => x.MailboxConnectionId == mailboxId);
        if (unreadOnly) query = query.Where(x => x.Labels.Contains("UNREAD"));
        var count = await query.CountAsync(cancellationToken);
        var messages = await query.OrderByDescending(x => x.ReceivedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new EmailSummary(x.Id, x.ThreadId, x.Subject, x.From, x.To,
                x.Snippet, x.ReceivedAt, !x.Labels.Contains("UNREAD"), x.HasAttachments)).ToListAsync(cancellationToken);
        return new EmailPage(messages, page, pageSize, count);
    }

    public async Task<EmailDetail> GetAsync(Guid userId, Guid mailboxId, Guid emailId, CancellationToken cancellationToken)
    {
        await RequireMailboxAsync(userId, mailboxId, cancellationToken);
        return await _dbContext.EmailMessages.AsNoTracking().Where(x => x.Id == emailId && x.MailboxConnectionId == mailboxId)
            .Select(x => new EmailDetail(x.Id, x.ThreadId, x.Subject, x.From, x.To, x.Snippet,
                x.BodyText, x.BodyTruncated, x.ReceivedAt, x.Labels, x.HasAttachments))
            .SingleOrDefaultAsync(cancellationToken) ?? throw new AppException(ErrorCode.NotFound, "The email was not found.");
    }

    private async Task RequireMailboxAsync(Guid userId, Guid mailboxId, CancellationToken cancellationToken)
    {
        var mailbox = await _dbContext.MailboxConnections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == mailboxId && x.UserId == userId, cancellationToken)
            ?? throw new AppException(ErrorCode.NotFound, "The mailbox was not found.");
        if (mailbox.Status != MailboxStatus.Connected)
            throw new AppException(ErrorCode.MailboxReconnectRequired, "Reconnect the mailbox before reading email.");
    }
}
