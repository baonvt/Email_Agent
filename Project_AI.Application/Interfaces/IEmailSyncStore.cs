using Project_AI.Application.DTOs.Emails;

namespace Project_AI.Application.Interfaces;

public interface IEmailSyncStore
{
    Task<MailboxSyncContext> BeginAsync(Guid userId, Guid mailboxId, CancellationToken cancellationToken);
    Task<MailboxSyncResult> CompleteAsync(MailboxSyncContext context, IReadOnlyList<ProviderEmail> messages,
        IReadOnlyList<string> removedIds, string historyId, bool replaceSnapshot, CancellationToken cancellationToken);
    Task FailAsync(MailboxSyncContext context, string errorCode, CancellationToken cancellationToken);
    Task<MailboxSyncStatus> GetStatusAsync(Guid userId, Guid mailboxId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MailboxSyncTarget>> ListDueAsync(DateTimeOffset attemptedBefore, CancellationToken cancellationToken);
}
