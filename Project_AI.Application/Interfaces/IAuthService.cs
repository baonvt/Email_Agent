using Project_AI.Application.DTOs.Auth;

namespace Project_AI.Application.Interfaces;

public interface IAuthService
{
    Task RegisterAsync(RegisterRequest request, CancellationToken ct);
    Task<AuthTokens> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<AuthTokens> RefreshAsync(string token, CancellationToken ct);
    Task ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken ct);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct);
    Task LogoutAsync(AccessTokenContext context, bool allSessions, CancellationToken ct);
    Task ResendConfirmationAsync(EmailRequest request, CancellationToken ct);
    Task ForgotPasswordAsync(EmailRequest request, CancellationToken ct);
    Task<UserProfile> GetProfileAsync(Guid id, CancellationToken ct);
}
