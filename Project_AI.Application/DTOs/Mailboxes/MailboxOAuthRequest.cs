using Project_AI.Application.DTOs.Auth;

namespace Project_AI.Application.DTOs.Mailboxes;

public sealed record MailboxOAuthRequest(AccessTokenContext Session, Guid MailboxId, Guid Version,
    string BrowserBindingHash, string CodeVerifier, DateTimeOffset ExpiresAt);
