namespace Project_AI.Application.Features.Auth;

public sealed class AuthService(IIdentityAccounts accounts, IAuthSessions sessions, IAuthEmailSender emails) : IAuthService
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
        ?? throw new AuthException(AuthErrorKind.Unauthorized, "invalid_session", "The session is no longer valid.");
}
