using Project_AI.Application.DTOs.Auth;

namespace Project_AI.Application.Interfaces;

public interface IIdentityAccounts
{
    Task<AuthAccount?> RegisterAsync(RegisterRequest request, CancellationToken ct);
    Task<AuthAccount?> FindByEmailAsync(string email, CancellationToken ct);
    Task<AuthAccount?> FindByIdAsync(Guid userId, CancellationToken ct);
    Task<AuthAccount> CheckPasswordAsync(LoginRequest request, CancellationToken ct);
    Task<string> GenerateConfirmationTokenAsync(Guid userId, CancellationToken ct);
    Task ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken ct);
    Task<string> GenerateResetTokenAsync(Guid userId, CancellationToken ct);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct);
}
