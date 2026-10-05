using Project_AI.Application.DTOs.Auth;

namespace Project_AI.Application.Interfaces;

public interface IAuthSessions
{
    Task<AuthTokens> CreateAsync(AuthAccount account, CancellationToken ct);
    Task<AuthTokens> RefreshAsync(string token, CancellationToken ct);
    Task RevokeAsync(AccessTokenContext context, bool allSessions, CancellationToken ct);
    Task<bool> ValidateAsync(AccessTokenContext context, CancellationToken ct);
}
