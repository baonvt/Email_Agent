using Project_AI.Application.DTOs.Auth;

namespace Project_AI.Application.Interfaces;

public interface IIdentityAccountService
{
    Task<AuthAccount?> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<AuthAccount?> FindByEmailAsync(string email, CancellationToken cancellationToken);
    Task<AuthAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<AuthAccount> CheckPasswordAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<string> GenerateConfirmationTokenAsync(Guid userId, CancellationToken cancellationToken);
    Task ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken);
    Task<string> GenerateResetTokenAsync(Guid userId, CancellationToken cancellationToken);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken);
}
