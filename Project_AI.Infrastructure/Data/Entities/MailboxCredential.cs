namespace Project_AI.Infrastructure.Data.Entities;

public sealed class MailboxCredential
{
    public Guid MailboxConnectionId { get; set; }
    public required string ProtectedPayload { get; set; }
    public DateTimeOffset AccessTokenExpiresAt { get; set; }
    public DateTimeOffset? RefreshTokenExpiresAt { get; set; }
}
