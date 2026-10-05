using Project_AI.Application.DTOs.Auth;

namespace Project_AI.Application.Interfaces;

public interface IAuthService
{
    Task RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<AuthTokens> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<AuthTokens> RefreshAsync(string token, CancellationToken cancellationToken);
    Task ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken);
    Task LogoutAsync(AccessTokenContext context, bool allSessions, CancellationToken cancellationToken);
    Task ResendConfirmationAsync(EmailRequest request, CancellationToken cancellationToken);
    Task ForgotPasswordAsync(EmailRequest request, CancellationToken cancellationToken);
    Task<UserProfile> GetProfileAsync(Guid id, CancellationToken cancellationToken);
}
