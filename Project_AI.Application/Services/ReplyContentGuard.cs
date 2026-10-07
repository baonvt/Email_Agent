using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Replies;

namespace Project_AI.Application.Services;

internal static class ReplyContentGuard
{
    public static void EnsureCurrentSource(ReplySource source, ReplyThread thread)
    {
        var cached = source.Email; var current = thread.Source;
        if (cached.MessageId != current.MessageId || cached.ThreadId != current.ThreadId
            || cached.Subject != current.Subject || cached.From != current.From || cached.To != current.To
            || cached.Snippet != current.Snippet || cached.BodyText != current.BodyText
            || cached.BodyTruncated != current.BodyTruncated || cached.ReceivedAt != current.ReceivedAt)
            throw new AppException(ErrorCode.DraftOutdated, "Gmail content differs from the cache. Synchronize first.");
    }
}
