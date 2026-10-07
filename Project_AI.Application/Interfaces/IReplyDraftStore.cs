using Project_AI.Application.DTOs.Replies;

namespace Project_AI.Application.Interfaces;

public interface IReplyDraftStore
{
    Task<ReplyGenerationContext> BeginGenerationAsync(Guid userId, Guid mailboxId, Guid emailId, bool force, Guid? expectedVersion, CancellationToken cancellationToken);
    Task<ReplyDraftResponse> CompleteGenerationAsync(ReplyGenerationContext context, ReplyThread thread, GeneratedReply output, CancellationToken cancellationToken);
    Task FailGenerationAsync(ReplyGenerationContext context, string errorCode, CancellationToken cancellationToken);
    Task<ReplyDraftResponse> GetAsync(Guid userId, Guid mailboxId, Guid draftId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReplyDraftResponse>> ListAsync(Guid userId, Guid mailboxId, Guid emailId, CancellationToken cancellationToken);
    Task<ReplyDraftResponse> EditAsync(Guid userId, Guid mailboxId, Guid draftId, Guid expectedVersion, string body, CancellationToken cancellationToken);
    Task DeleteAsync(Guid userId, Guid mailboxId, Guid draftId, Guid expectedVersion, CancellationToken cancellationToken);
}
