namespace Project_AI.Infrastructure.Data.Entities;

// Provider checkpoint and expiring lease are persistence details, separate from email content.
public sealed class MailboxSyncState
{
    public Guid MailboxConnectionId { get; set; }
    public string? HistoryId { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public string? LastErrorCode { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
}
