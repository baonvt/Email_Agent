using Project_AI.Application.DTOs.Auth;

namespace Project_AI.Application.Interfaces;

public interface IAuthSessionService
{
    Task<AuthTokens> CreateAsync(AuthAccount account, CancellationToken cancellationToken);
    Task<AuthTokens> RefreshAsync(string token, CancellationToken cancellationToken);
    Task RevokeAsync(AccessTokenContext context, bool allSessions, CancellationToken cancellationToken);
    Task<bool> ValidateAsync(AccessTokenContext context, CancellationToken cancellationToken);
}
