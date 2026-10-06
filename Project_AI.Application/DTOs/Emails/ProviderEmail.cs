namespace Project_AI.Application.DTOs.Emails;

public sealed record ProviderEmail(string MessageId, string ThreadId, string Subject, string From,
    string To, string Snippet, string BodyText, bool BodyTruncated, bool HasAttachments,
    string[] Labels, DateTimeOffset ReceivedAt);
