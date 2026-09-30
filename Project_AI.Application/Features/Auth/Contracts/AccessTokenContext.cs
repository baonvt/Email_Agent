namespace Project_AI.Application.Features.Auth.Contracts;

public sealed record AccessTokenContext(Guid UserId, Guid SessionId, string Jti,
    string SecurityStamp, DateTimeOffset ExpiresAt);
