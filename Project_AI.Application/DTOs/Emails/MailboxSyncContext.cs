namespace Project_AI.Application.DTOs.Emails;

public sealed record MailboxSyncContext(Guid UserId, Guid MailboxId, Guid MailboxVersion,
    Guid LeaseId, string? HistoryId);
public sealed record MailboxSyncTarget(Guid UserId, Guid MailboxId);
