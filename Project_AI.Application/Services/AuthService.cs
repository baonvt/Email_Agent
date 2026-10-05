using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Auth;
using Project_AI.Application.Interfaces;

namespace Project_AI.Application.Services;

public sealed class AuthService(IIdentityAccountService accounts, IAuthSessionService sessions, IAuthEmailSender emails) : IAuthService
{
    public async Task RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var account = await accounts.RegisterAsync(request, ct);
        // A duplicate registration has the same public response, without sending unsolicited mail.
        if (account is not null)
            await emails.QueueConfirmationAsync(account,
                await accounts.GenerateConfirmationTokenAsync(account.Profile.Id, ct), ct);
    }

    public async Task<AuthTokens> LoginAsync(LoginRequest request, CancellationToken ct) =>
        await sessions.CreateAsync(await accounts.CheckPasswordAsync(request, ct), ct);

    public Task<AuthTokens> RefreshAsync(string token, CancellationToken ct) => sessions.RefreshAsync(token, ct);
    public Task ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken ct) => accounts.ConfirmEmailAsync(request, ct);
    public Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct) => accounts.ResetPasswordAsync(request, ct);
    public Task LogoutAsync(AccessTokenContext context, bool allSessions, CancellationToken ct) =>
        sessions.RevokeAsync(context, allSessions, ct);

    public async Task ResendConfirmationAsync(EmailRequest request, CancellationToken ct)
    {
        var account = await accounts.FindByEmailAsync(request.Email, ct);
        if (account is { EmailConfirmed: false })
            await emails.QueueConfirmationAsync(account,
                await accounts.GenerateConfirmationTokenAsync(account.Profile.Id, ct), ct);
    }

    public async Task ForgotPasswordAsync(EmailRequest request, CancellationToken ct)
    {
        var account = await accounts.FindByEmailAsync(request.Email, ct);
        if (account is { EmailConfirmed: true })
            await emails.QueuePasswordResetAsync(account,
                await accounts.GenerateResetTokenAsync(account.Profile.Id, ct), ct);
    }

    public async Task<UserProfile> GetProfileAsync(Guid id, CancellationToken ct) =>
        (await accounts.FindByIdAsync(id, ct))?.Profile
        ?? throw new AppException(ErrorCode.InvalidSession, "The session is no longer valid.");
}
