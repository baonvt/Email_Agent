using Project_AI.Application.DTOs.Auth;
using Project_AI.Application.DTOs.Replies;

namespace Project_AI.Application.Interfaces;

public interface IReplySendService
{
    Task<ReplyDraftResponse> SendAsync(AccessTokenContext session, Guid mailboxId, Guid draftId,
        Guid expectedVersion, CancellationToken cancellationToken);
}
