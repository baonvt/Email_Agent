using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;



namespace Project_AI.API.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public sealed class AuthController(IAuthService auth, IOptions<AuthWebOptions> options) : ControllerBase
{
    [HttpPost("register"), AllowAnonymous]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        await auth.RegisterAsync(request, ct);
        return Accepted(new { message = "If registration is eligible, a confirmation email will be sent. Check your inbox." });
    }

    [HttpPost("confirm-email"), AllowAnonymous]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailRequest request, CancellationToken ct)
    {
        await auth.ConfirmEmailAsync(request, ct);
        return NoContent();
    }

    [HttpPost("resend-confirmation"), AllowAnonymous]
    public async Task<IActionResult> ResendConfirmation(EmailRequest request, CancellationToken ct)
    {
        await auth.ResendConfirmationAsync(request, ct);
        return EmailAccepted();
    }

    [HttpPost("forgot-password"), AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(EmailRequest request, CancellationToken ct)
    {
        await auth.ForgotPasswordAsync(request, ct);
        return EmailAccepted();
    }

    [HttpPost("reset-password"), AllowAnonymous]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        await auth.ResetPasswordAsync(request, ct);
        DeleteRefreshCookie();
        return NoContent();
    }

    [HttpPost("login"), AllowAnonymous]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request, CancellationToken ct) =>
        WriteTokens(await auth.LoginAsync(request, ct));

    [HttpPost("refresh"), AllowAnonymous]
    public async Task<ActionResult<TokenResponse>> Refresh(CancellationToken ct)
    {
        var token = Request.Cookies[options.Value.CookieName];
        try
        {
            return WriteTokens(await auth.RefreshAsync(token ?? "", ct));
        }
        catch (AuthException ex) when (ex.Kind == AuthErrorKind.Unauthorized)
        {
            DeleteRefreshCookie();
            throw;
        }
    }

    [HttpPost("logout"), Authorize]
    public Task<IActionResult> Logout(CancellationToken ct) => LogoutCore(false, ct);

    [HttpPost("logout-all"), Authorize]
    public Task<IActionResult> LogoutAll(CancellationToken ct) => LogoutCore(true, ct);

    [HttpGet("me"), Authorize]
    public Task<UserProfile> Me(CancellationToken ct) => auth.GetProfileAsync(CurrentToken().UserId, ct);

    private async Task<IActionResult> LogoutCore(bool all, CancellationToken ct)
    {
        try { await auth.LogoutAsync(CurrentToken(), all, ct); }
        finally { DeleteRefreshCookie(); }
        return NoContent();
    }

    private TokenResponse WriteTokens(AuthTokens tokens)
    {
        var cookie = CookieSettings();
        cookie.Expires = tokens.RefreshTokenExpiresAt;
        Response.Cookies.Append(options.Value.CookieName, tokens.RefreshToken, cookie);
        return new TokenResponse(tokens.AccessToken, tokens.AccessTokenExpiresAt, tokens.User);
    }

    private void DeleteRefreshCookie() => Response.Cookies.Delete(options.Value.CookieName, CookieSettings());
    private CookieOptions CookieSettings() => new()
    {
        HttpOnly = true,
        Secure = !options.Value.AllowInsecureCookiesForDevelopment || Request.IsHttps,
        SameSite = Enum.Parse<SameSiteMode>(options.Value.SameSite),
        Path = "/", IsEssential = true
    };
    private AccessTokenContext CurrentToken() => AccessTokenClaims.Read(User)
        ?? throw new AuthException(AuthErrorKind.Unauthorized, "invalid_session", "The session is no longer valid.");
    private AcceptedResult EmailAccepted() => Accepted(new { message = "If the account is eligible, an email will be sent." });
}
