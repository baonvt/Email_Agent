using Project_AI.Application.DTOs.Emails;

namespace Project_AI.Application.Interfaces;

public interface IEmailSyncService
{
    Task<MailboxSyncResult> SyncAsync(Guid userId, Guid mailboxId, CancellationToken cancellationToken);
}
