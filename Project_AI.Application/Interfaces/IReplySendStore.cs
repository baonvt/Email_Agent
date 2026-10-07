using Project_AI.Application.DTOs.Auth;
using Project_AI.Application.DTOs.Replies;

namespace Project_AI.Application.Interfaces;

public interface IReplySendStore
{
    Task<ReplySendContext> ReadAsync(Guid userId, Guid mailboxId, Guid draftId, Guid expectedVersion, CancellationToken cancellationToken);
    Task<ReplySendContext> BeginAsync(AccessTokenContext session, Guid mailboxId, Guid draftId, Guid expectedVersion, CancellationToken cancellationToken);
    Task<ReplyDraftResponse> CompleteAsync(ReplySendContext context, ReplySendResult result, CancellationToken cancellationToken);
    Task FailAsync(ReplySendContext context, bool outcomeUnknown, string errorCode, CancellationToken cancellationToken);
}
