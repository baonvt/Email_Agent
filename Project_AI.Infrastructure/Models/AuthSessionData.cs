using Project_AI.Application.DTOs.Auth;

namespace Project_AI.Infrastructure.Models;

public sealed record AuthSessionData(Guid SessionId, DateTimeOffset ExpiresAt, AuthAccount Account);
