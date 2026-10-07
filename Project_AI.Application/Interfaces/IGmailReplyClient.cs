using Project_AI.Application.DTOs.Replies;

namespace Project_AI.Application.Interfaces;

public interface IGmailReplyClient
{
    Task<ReplyThread> GetThreadAsync(ReplySource source, CancellationToken cancellationToken);
    Task<ReplySendResult> SendAsync(ReplySendContext context, CancellationToken cancellationToken);
}
