using Project_AI.Application.DTOs.Replies;

namespace Project_AI.Application.Interfaces;

public interface IReplyGenerator
{
    Task<GeneratedReply> GenerateAsync(ReplySource source, ReplyThread thread, CancellationToken cancellationToken);
}
