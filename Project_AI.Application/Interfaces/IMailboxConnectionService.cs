using Project_AI.Application.DTOs.Auth;
using Project_AI.Application.DTOs.Mailboxes;

namespace Project_AI.Application.Interfaces;

public interface IMailboxConnectionService
{
    Task<MailboxAuthorization> BeginAsync(AccessTokenContext session, string browserBinding, CancellationToken cancellationToken);
    Task<MailboxResponse> CompleteAsync(string state, string? code, string? error, string browserBinding,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<MailboxResponse>> ListAsync(Guid userId, CancellationToken cancellationToken);
    Task DisconnectAsync(AccessTokenContext session, CancellationToken cancellationToken);
}
