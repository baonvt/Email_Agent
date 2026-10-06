using Project_AI.Application.DTOs.Emails;

namespace Project_AI.Application.Interfaces;

public interface IGmailMessageClient
{
    Task<string> GetHistoryIdAsync(Guid userId, Guid mailboxId, CancellationToken cancellationToken);
    Task<GmailMessagePage> ListInboxAsync(Guid userId, Guid mailboxId, int limit, string? pageToken, CancellationToken cancellationToken);
    Task<ProviderEmail?> GetMessageAsync(Guid userId, Guid mailboxId, string messageId, CancellationToken cancellationToken);
    Task<GmailHistoryPage> ListHistoryAsync(Guid userId, Guid mailboxId, string historyId, string? pageToken, CancellationToken cancellationToken);
}
