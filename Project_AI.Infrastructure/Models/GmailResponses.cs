namespace Project_AI.Infrastructure.Models;

internal sealed record GmailListResult(GmailMessageReference[]? Messages, string? NextPageToken);
internal sealed record GmailMessageReference(string Id);
internal sealed record GmailProfileResult(string HistoryId);
internal sealed record GmailHistoryResult(GmailHistoryRecord[]? History, string HistoryId, string? NextPageToken);
internal sealed record GmailHistoryRecord(GmailMessageReference[]? Messages,
    GmailHistoryChange[]? MessagesAdded, GmailHistoryChange[]? MessagesDeleted,
    GmailHistoryChange[]? LabelsAdded, GmailHistoryChange[]? LabelsRemoved);
internal sealed record GmailHistoryChange(GmailMessageReference Message);
internal sealed record GmailMessageResult(string Id, string ThreadId, string[]? LabelIds,
    string? Snippet, string InternalDate, GmailPart? Payload);
internal sealed record GmailPart(string? MimeType, string? Filename, GmailHeader[]? Headers,
    GmailBody? Body, GmailPart[]? Parts);
internal sealed record GmailHeader(string Name, string Value);
internal sealed record GmailBody(string? Data, string? AttachmentId);
