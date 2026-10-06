namespace Project_AI.Infrastructure.Data.Entities;

// Expiring analysis lease and retry diagnostics belong to persistence.
public sealed class EmailAnalysisRequest
{
    public Guid EmailMessageId { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public string? LastErrorCode { get; set; }
}
