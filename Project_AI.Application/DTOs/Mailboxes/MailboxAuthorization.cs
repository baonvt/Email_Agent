namespace Project_AI.Application.DTOs.Mailboxes;

public sealed record MailboxAuthorization(string AuthorizationUrl, DateTimeOffset ExpiresAt);
