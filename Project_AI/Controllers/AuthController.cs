using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Project_AI.API.Options;
using Project_AI.API.Security;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Auth;
using Project_AI.Application.Interfaces;

namespace Project_AI.API.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IOptions<AuthWebOptions> _authWebOptions;

    public AuthController(IAuthService authService, IOptions<AuthWebOptions> authWebOptions)
    {
        _authService = authService;
        _authWebOptions = authWebOptions;
    }

    [HttpPost("register"), AllowAnonymous]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        await _authService.RegisterAsync(request, cancellationToken);
        return Accepted(new { message = "If registration is eligible, a confirmation email will be sent. Check your inbox." });
    }

    [HttpPost("confirm-email"), AllowAnonymous]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        await _authService.ConfirmEmailAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpPost("resend-confirmation"), AllowAnonymous]
    public async Task<IActionResult> ResendConfirmation(EmailRequest request, CancellationToken cancellationToken)
    {
        await _authService.ResendConfirmationAsync(request, cancellationToken);
        return EmailAccepted();
    }

    [HttpPost("forgot-password"), AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(EmailRequest request, CancellationToken cancellationToken)
    {
        await _authService.ForgotPasswordAsync(request, cancellationToken);
        return EmailAccepted();
    }

    [HttpPost("reset-password"), AllowAnonymous]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await _authService.ResetPasswordAsync(request, cancellationToken);
        DeleteRefreshCookie();
        return NoContent();
    }

    [HttpPost("login"), AllowAnonymous]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request, CancellationToken cancellationToken) =>
        WriteTokens(await _authService.LoginAsync(request, cancellationToken));

    [HttpPost("refresh"), AllowAnonymous]
    public async Task<ActionResult<TokenResponse>> Refresh(CancellationToken cancellationToken)
    {
        var token = Request.Cookies[_authWebOptions.Value.CookieName];
        try
        {
            return WriteTokens(await _authService.RefreshAsync(token ?? "", cancellationToken));
        }
        catch (AppException ex) when (ex.Code == ErrorCode.InvalidSession)
        {
            DeleteRefreshCookie();
            throw;
        }
    }

    [HttpPost("logout"), Authorize]
    public Task<IActionResult> Logout(CancellationToken cancellationToken) => LogoutCore(false, cancellationToken);

    [HttpPost("logout-all"), Authorize]
    public Task<IActionResult> LogoutAll(CancellationToken cancellationToken) => LogoutCore(true, cancellationToken);

    [HttpGet("me"), Authorize]
    public Task<UserProfile> Me(CancellationToken cancellationToken) => _authService.GetProfileAsync(CurrentToken().UserId, cancellationToken);

    private async Task<IActionResult> LogoutCore(bool all, CancellationToken cancellationToken)
    {
        try
        {
            await _authService.LogoutAsync(CurrentToken(), all, cancellationToken);
        }
        finally { DeleteRefreshCookie(); }
        return NoContent();
    }

    private TokenResponse WriteTokens(AuthTokens tokens)
    {
        var cookie = CookieSettings();
        cookie.Expires = tokens.RefreshTokenExpiresAt;
        Response.Cookies.Append(_authWebOptions.Value.CookieName, tokens.RefreshToken, cookie);
        return new TokenResponse(tokens.AccessToken, tokens.AccessTokenExpiresAt, tokens.User);
    }

    private void DeleteRefreshCookie() => Response.Cookies.Delete(_authWebOptions.Value.CookieName, CookieSettings());
    private CookieOptions CookieSettings() => new()
    {
        HttpOnly = true,
        Secure = !_authWebOptions.Value.AllowInsecureCookiesForDevelopment || Request.IsHttps,
        SameSite = Enum.Parse<SameSiteMode>(_authWebOptions.Value.SameSite),
        Path = "/", IsEssential = true
    };
    private AccessTokenContext CurrentToken() => AccessTokenClaims.Read(User)
        ?? throw new AppException(ErrorCode.InvalidSession, "The session is no longer valid.");
    private AcceptedResult EmailAccepted() => Accepted(new { message = "If the account is eligible, an email will be sent." });
}
