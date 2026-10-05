using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Project_AI.API.Models;
using Project_AI.API.Options;
using Project_AI.API.Security;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Auth;
using Project_AI.Application.DTOs.Mailboxes;
using Project_AI.Application.Interfaces;

namespace Project_AI.API.Controllers;

[ApiController]
[Route("api/mailboxes")]
public sealed class MailboxesController : ControllerBase
{
    private readonly IMailboxConnectionService _mailboxes;
    private readonly IOptions<AuthWebOptions> _webOptions;

    public MailboxesController(IMailboxConnectionService mailboxes, IOptions<AuthWebOptions> webOptions)
    {
        _mailboxes = mailboxes;
        _webOptions = webOptions;
    }

    [HttpGet]
    public Task<IReadOnlyList<MailboxResponse>> List(CancellationToken cancellationToken) =>
        _mailboxes.ListAsync(CurrentSession().UserId, cancellationToken);

    [HttpPost("gmail/connect"), EnableRateLimiting("auth")]
    public async Task<MailboxAuthorization> ConnectGmail(CancellationToken cancellationToken)
    {
        var binding = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var result = await _mailboxes.BeginAsync(CurrentSession(), binding, cancellationToken);
        var cookie = CookieSettings();
        cookie.Expires = result.ExpiresAt;
        Response.Cookies.Append(CookieName, binding, cookie);
        return result;
    }

    [HttpGet("gmail/callback"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> GmailCallback([FromQuery] GmailCallbackQuery query, CancellationToken cancellationToken)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (Request.Query["state"].Count != 1 || Request.Query["code"].Count > 1
            || Request.Query["error"].Count > 1 || Request.Query["iss"].Count > 1
            || (query.Issuer is not null && query.Issuer != "https://accounts.google.com"))
        {
            throw new AppException(ErrorCode.InvalidOAuthState, "The authorization callback is invalid.");
        }

        try
        {
            await _mailboxes.CompleteAsync(query.State, query.Code, query.Error,
                Request.Cookies[CookieName] ?? "", cancellationToken);
            DeleteCookie();
            // Clear the authorization code from the browser URL without relying on a frontend page.
            return RedirectToAction(nameof(GmailConnectionResult));
        }
        catch (AppException exception) when (exception.Code != ErrorCode.InvalidOAuthState)
        {
            DeleteCookie();
            throw;
        }
    }

    [HttpGet("gmail/result"), AllowAnonymous]
    public IActionResult GmailConnectionResult() => Ok(new
    {
        message = "Gmail authorization completed. Use the authenticated GET /api/mailboxes endpoint to view your connection."
    });

    [HttpPost("gmail/disconnect"), EnableRateLimiting("auth")]
    public async Task<IActionResult> DisconnectGmail(CancellationToken cancellationToken)
    {
        await _mailboxes.DisconnectAsync(CurrentSession(), cancellationToken);
        DeleteCookie();
        return NoContent();
    }

    private string CookieName => _webOptions.Value.AllowInsecureCookiesForDevelopment
        ? "inboxagent.gmail" : "__Host-inboxagent.gmail";
    private CookieOptions CookieSettings() => new()
    {
        HttpOnly = true,
        Secure = !_webOptions.Value.AllowInsecureCookiesForDevelopment || Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        IsEssential = true
    };
    private void DeleteCookie() => Response.Cookies.Delete(CookieName, CookieSettings());
    private AccessTokenContext CurrentSession() => AccessTokenClaims.Read(User)
        ?? throw new AppException(ErrorCode.InvalidSession, "The InboxAgent session is no longer valid.");
}
