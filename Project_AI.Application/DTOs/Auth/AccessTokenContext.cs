namespace Project_AI.Application.DTOs.Auth;

public sealed record AccessTokenContext(Guid UserId, Guid SessionId, string Jti,
    string SecurityStamp, DateTimeOffset ExpiresAt);
