using Project_AI.Application.DTOs.Auth;

namespace Project_AI.Application.Interfaces;

public interface IAuthSessionRepository
{
    Task<AuthSessionData> CreateAsync(AuthAccount account, string refreshTokenHash,
        DateTimeOffset expiresAt, CancellationToken ct);
    Task<AuthSessionData> RotateAsync(string currentTokenHash, string replacementTokenHash, CancellationToken ct);
    Task RevokeAsync(AccessTokenContext context, bool allSessions, CancellationToken ct);
    Task<bool> IsActiveAsync(AccessTokenContext context, CancellationToken ct);
}
