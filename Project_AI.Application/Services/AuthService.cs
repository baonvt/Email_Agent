using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Auth;
using Project_AI.Application.Interfaces;

namespace Project_AI.Application.Services;

public sealed class AuthService : IAuthService
{
    private readonly IIdentityAccountService _identityAccountService;
    private readonly IAuthSessionService _authSessionService;
    private readonly IAuthEmailSender _emailSender;

    public AuthService(
        IIdentityAccountService identityAccountService,
        IAuthSessionService authSessionService,
        IAuthEmailSender emailSender)
    {
        _identityAccountService = identityAccountService;
        _authSessionService = authSessionService;
        _emailSender = emailSender;
    }

    public async Task RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var account = await _identityAccountService.RegisterAsync(request, cancellationToken);
        // A duplicate registration has the same public response, without sending unsolicited mail.
        if (account is null)
        {
            return;
        }

        var token = await _identityAccountService.GenerateConfirmationTokenAsync(account.Profile.Id, cancellationToken);
        await _emailSender.QueueConfirmationAsync(account, token, cancellationToken);
    }

    public async Task<AuthTokens> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var account = await _identityAccountService.CheckPasswordAsync(request, cancellationToken);
        return await _authSessionService.CreateAsync(account, cancellationToken);
    }

    public Task<AuthTokens> RefreshAsync(string token, CancellationToken cancellationToken) => _authSessionService.RefreshAsync(token, cancellationToken);
    public Task ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken) => _identityAccountService.ConfirmEmailAsync(request, cancellationToken);
    public Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken) => _identityAccountService.ResetPasswordAsync(request, cancellationToken);
    public Task LogoutAsync(AccessTokenContext context, bool allSessions, CancellationToken cancellationToken) =>
        _authSessionService.RevokeAsync(context, allSessions, cancellationToken);

    public async Task ResendConfirmationAsync(EmailRequest request, CancellationToken cancellationToken)
    {
        var account = await _identityAccountService.FindByEmailAsync(request.Email, cancellationToken);
        if (account is { EmailConfirmed: false })
        {
            var token = await _identityAccountService.GenerateConfirmationTokenAsync(account.Profile.Id, cancellationToken);
            await _emailSender.QueueConfirmationAsync(account, token, cancellationToken);
        }
    }

    public async Task ForgotPasswordAsync(EmailRequest request, CancellationToken cancellationToken)
    {
        var account = await _identityAccountService.FindByEmailAsync(request.Email, cancellationToken);
        if (account is { EmailConfirmed: true })
        {
            var token = await _identityAccountService.GenerateResetTokenAsync(account.Profile.Id, cancellationToken);
            await _emailSender.QueuePasswordResetAsync(account, token, cancellationToken);
        }
    }

    public async Task<UserProfile> GetProfileAsync(Guid id, CancellationToken cancellationToken) =>
        (await _identityAccountService.FindByIdAsync(id, cancellationToken))?.Profile
        ?? throw new AppException(ErrorCode.InvalidSession, "The session is no longer valid.");
}
