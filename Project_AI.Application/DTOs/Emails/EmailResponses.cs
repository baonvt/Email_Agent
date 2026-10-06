namespace Project_AI.Application.DTOs.Emails;

public sealed record EmailSummary(Guid Id, string ThreadId, string Subject, string From, string To,
    string Snippet, DateTimeOffset ReceivedAt, bool IsRead, bool HasAttachments);
public sealed record EmailDetail(Guid Id, string ThreadId, string Subject, string From, string To,
    string Snippet, string BodyText, bool BodyTruncated, DateTimeOffset ReceivedAt, string[] Labels,
    bool HasAttachments);
public sealed record EmailPage(IReadOnlyList<EmailSummary> Items, int Page, int PageSize, int TotalCount);
public sealed record MailboxSyncStatus(Guid MailboxId, DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSyncedAt, string? LastErrorCode, bool IsSyncing);
public sealed record MailboxSyncResult(Guid MailboxId, string Mode, int Updated, int Removed, DateTimeOffset SyncedAt);
