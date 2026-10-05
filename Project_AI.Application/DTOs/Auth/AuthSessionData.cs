namespace Project_AI.Application.DTOs.Auth;

public sealed record AuthSessionData(Guid SessionId, DateTimeOffset ExpiresAt, AuthAccount Account);
