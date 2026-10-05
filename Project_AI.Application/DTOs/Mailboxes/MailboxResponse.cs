namespace Project_AI.Application.DTOs.Mailboxes;

public sealed record MailboxResponse(Guid Id, string Provider, string? Email, string Status,
    DateTimeOffset? ConnectedAt, DateTimeOffset UpdatedAt);
