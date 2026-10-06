using Project_AI.Application.DTOs.Emails;

namespace Project_AI.Application.Interfaces;

public interface IEmailQueryStore
{
    Task<EmailPage> ListAsync(Guid userId, Guid mailboxId, int page, int pageSize, bool unreadOnly, CancellationToken cancellationToken);
    Task<EmailDetail> GetAsync(Guid userId, Guid mailboxId, Guid emailId, CancellationToken cancellationToken);
}
