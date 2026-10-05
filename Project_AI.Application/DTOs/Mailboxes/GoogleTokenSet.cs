namespace Project_AI.Application.DTOs.Mailboxes;

// Internal provider credentials. Never serialize this record in an API response.
public sealed record GoogleTokenSet(string AccessToken, string? RefreshToken,
    DateTimeOffset AccessTokenExpiresAt, DateTimeOffset? RefreshTokenExpiresAt, string Scope);
