using Project_AI.Application.DTOs.Replies;

namespace Project_AI.Application.Interfaces;

public interface IReplyDraftService
{
    Task<ReplyDraftResponse> GenerateAsync(Guid userId, Guid mailboxId, Guid emailId,
        bool force, Guid? expectedVersion, CancellationToken cancellationToken);
}
