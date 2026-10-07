using Project_AI.Application.DTOs.Emails;
using Project_AI.Domain.Entities;

namespace Project_AI.Application.DTOs.Replies;

public sealed record ReplySource(Guid UserId, Guid MailboxId, Guid MailboxVersion, Guid SourceVersion,
    string MailboxEmail, ProviderEmail Email);
public sealed record ReplyGenerationContext(Guid DraftId, Guid GenerationId, ReplySource Source, ReplyDraftResponse? CachedDraft);
public sealed record ReplyThread(ProviderEmail Source, string To, string InReplyTo, string[] References,
    string Fingerprint, IReadOnlyList<ProviderEmail> Messages, bool Truncated);
public sealed record GeneratedReply(string BodyText, string Model, bool InputTruncated);
public sealed record ReplySendContext(ReplySource Source, ReplyDraft Draft);
public sealed record ReplySendResult(string MessageId, string ThreadId);
public sealed record ReplyDraftResponse(Guid Id, Guid EmailId, Guid Version, string Status, string From, string To,
    string Subject, string BodyText, bool IsOutdated, bool InputTruncated, string Model, string? ProviderMessageId,
    string OutgoingMessageId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? SentAt, string? LastErrorCode);
