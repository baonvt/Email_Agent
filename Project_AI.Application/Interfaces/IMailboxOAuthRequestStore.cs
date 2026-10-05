using Project_AI.Application.DTOs.Mailboxes;

namespace Project_AI.Application.Interfaces;

public interface IMailboxOAuthRequestStore
{
    Task SaveAsync(string state, MailboxOAuthRequest request, CancellationToken cancellationToken);
    Task<MailboxOAuthRequest?> ConsumeAsync(string state, string browserBindingHash, CancellationToken cancellationToken);
}
