using Microsoft.EntityFrameworkCore;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Analysis;
using Project_AI.Application.Interfaces;
using Project_AI.Domain.Entities;
using Project_AI.Domain.Enums;
using Project_AI.Infrastructure.Data;
using Project_AI.Infrastructure.Data.Entities;

namespace Project_AI.Infrastructure.Repositories;

public sealed class EmailAnalysisRepository : IEmailAnalysisStore
{
    private readonly AuthDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public EmailAnalysisRepository(AuthDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<EmailAnalysisContext> BeginAsync(Guid userId, Guid mailboxId, Guid emailId, bool force, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var mailbox = await LockMailboxAsync(userId, mailboxId, cancellationToken);
        var email = await ReadEmailAsync(mailboxId, emailId, cancellationToken);
        var input = new EmailAnalysisInput(email.Id, email.ContentVersion, email.Subject, email.From, email.To,
            email.Snippet, email.BodyText, email.BodyTruncated, email.ReceivedAt);
        var result = await _dbContext.EmailAnalyses.AsNoTracking().SingleOrDefaultAsync(x => x.EmailMessageId == emailId, cancellationToken);
        if (!force && result?.SourceVersion == email.ContentVersion)
            return new EmailAnalysisContext(userId, mailboxId, mailbox.Version, input, Guid.Empty, Map(result));
        var now = _timeProvider.GetUtcNow();
        var state = await _dbContext.EmailAnalysisRequests.AsNoTracking().SingleOrDefaultAsync(x => x.EmailMessageId == emailId, cancellationToken);
        if (state?.LeaseExpiresAt > now)
            throw new AppException(ErrorCode.AnalysisInProgress, "An analysis is already running.");
        if (state is null)
        {
            state = new EmailAnalysisRequest { EmailMessageId = emailId };
            _dbContext.EmailAnalysisRequests.Add(state);
        }
        else _dbContext.EmailAnalysisRequests.Update(state);
        state.LeaseId = Guid.NewGuid();
        state.LeaseExpiresAt = now.AddMinutes(2);
        state.LastAttemptAt = now;
        state.LastErrorCode = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _dbContext.Entry(state).State = EntityState.Detached;
        return new EmailAnalysisContext(userId, mailboxId, mailbox.Version, input, state.LeaseId.Value, null);
    }

    public async Task<EmailAnalysisResponse> CompleteAsync(EmailAnalysisContext context, EmailAnalysisOutput output, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var mailbox = await LockMailboxAsync(context.UserId, context.MailboxId, cancellationToken);
        var email = await ReadEmailAsync(context.MailboxId, context.Email.EmailId, cancellationToken);
        var state = await _dbContext.EmailAnalysisRequests.AsNoTracking()
            .SingleOrDefaultAsync(x => x.EmailMessageId == email.Id, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        if (mailbox.Version != context.MailboxVersion || email.ContentVersion != context.Email.SourceVersion
            || state is null || state.LeaseId != context.LeaseId || state.LeaseExpiresAt <= now)
            throw new AppException(ErrorCode.AnalysisOutdated, "The email, mailbox or analysis lease changed. Start again.");
        var result = await _dbContext.EmailAnalyses.AsNoTracking().SingleOrDefaultAsync(x => x.EmailMessageId == email.Id, cancellationToken);
        if (result is null)
        {
            result = new EmailAnalysis(email.Id);
            _dbContext.EmailAnalyses.Add(result);
        }
        else _dbContext.EmailAnalyses.Update(result);
        result.Update(email.ContentVersion, output.Summary, output.Category, output.Priority, output.Confidence,
            output.InputTruncated || email.BodyTruncated, output.Provider, output.Model, now);
        state.LeaseId = null;
        state.LeaseExpiresAt = null;
        state.LastErrorCode = null;
        _dbContext.EmailAnalysisRequests.Update(state);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _dbContext.Entry(state).State = EntityState.Detached;
        _dbContext.Entry(result).State = EntityState.Detached;
        return Map(result);
    }

    public Task FailAsync(EmailAnalysisContext context, string errorCode, CancellationToken cancellationToken) =>
        _dbContext.EmailAnalysisRequests.Where(x => x.EmailMessageId == context.Email.EmailId && x.LeaseId == context.LeaseId)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.LastErrorCode, errorCode)
                .SetProperty(x => x.LeaseId, (Guid?)null).SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null), cancellationToken);

    public async Task<EmailAnalysisStatus> GetAsync(Guid userId, Guid mailboxId, Guid emailId, CancellationToken cancellationToken)
    {
        // Use the mailbox lock briefly so source version, result and status form one consistent read.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockMailboxAsync(userId, mailboxId, cancellationToken);
        var email = await ReadEmailAsync(mailboxId, emailId, cancellationToken);
        var result = await _dbContext.EmailAnalyses.AsNoTracking().SingleOrDefaultAsync(x => x.EmailMessageId == emailId, cancellationToken);
        var state = await _dbContext.EmailAnalysisRequests.AsNoTracking().SingleOrDefaultAsync(x => x.EmailMessageId == emailId, cancellationToken);
        var current = result?.SourceVersion == email.ContentVersion ? Map(result) : null;
        var status = state?.LeaseExpiresAt > _timeProvider.GetUtcNow() ? "processing"
            : state?.LastErrorCode is not null ? "failed" : current is not null ? "completed"
            : result is not null ? "outdated" : "not_started";
        return new EmailAnalysisStatus(status, current, state?.LastAttemptAt, state?.LastErrorCode);
    }

    private async Task<MailboxConnection> LockMailboxAsync(Guid userId, Guid mailboxId, CancellationToken cancellationToken)
    {
        var mailbox = (await _dbContext.MailboxConnections.FromSqlInterpolated(
            $"SELECT * FROM \"MailboxConnections\" WHERE \"Id\" = {mailboxId} AND \"UserId\" = {userId} FOR UPDATE")
            .AsNoTracking().ToListAsync(cancellationToken)).SingleOrDefault()
            ?? throw new AppException(ErrorCode.NotFound, "The mailbox was not found.");
        if (mailbox.Status != MailboxStatus.Connected)
            throw new AppException(ErrorCode.MailboxReconnectRequired, "Reconnect the mailbox before analyzing emails.");
        return mailbox;
    }

    private async Task<EmailMessage> ReadEmailAsync(Guid mailboxId, Guid emailId, CancellationToken cancellationToken) =>
        await _dbContext.EmailMessages.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == emailId && x.MailboxConnectionId == mailboxId, cancellationToken)
        ?? throw new AppException(ErrorCode.NotFound, "The email was not found.");

    private static EmailAnalysisResponse Map(EmailAnalysis result) => new(result.EmailMessageId, result.Summary,
        result.Category.ToString().ToLowerInvariant(), result.Priority.ToString().ToLowerInvariant(), result.Confidence,
        result.InputTruncated, result.Provider, result.Model, result.AnalyzedAt);
}
