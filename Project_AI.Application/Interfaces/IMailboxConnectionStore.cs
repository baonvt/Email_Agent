using Project_AI.Application.DTOs.Auth;
using Project_AI.Application.DTOs.Mailboxes;
using Project_AI.Domain.Entities;

namespace Project_AI.Application.Interfaces;

public interface IMailboxConnectionStore
{
    Task<MailboxConnection> GetOrCreateAsync(AccessTokenContext session, CancellationToken cancellationToken);
    Task<IReadOnlyList<MailboxConnection>> ListAsync(Guid userId, CancellationToken cancellationToken);
    Task<MailboxConnection> ConnectAsync(AccessTokenContext session, Guid mailboxId, Guid expectedVersion,
        GoogleMailboxAccount account, GoogleTokenSet tokens, CancellationToken cancellationToken);
    Task DisconnectAsync(AccessTokenContext session, CancellationToken cancellationToken);
}
