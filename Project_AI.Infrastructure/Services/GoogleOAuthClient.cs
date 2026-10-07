using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Mailboxes;
using Project_AI.Application.Interfaces;
using Project_AI.Infrastructure.Options;

namespace Project_AI.Infrastructure.Services;

public sealed class GoogleOAuthClient : IGoogleOAuthClient
{
    public const string ReadOnlyScope = "https://www.googleapis.com/auth/gmail.readonly";
    public const string SendScope = "https://www.googleapis.com/auth/gmail.send";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private readonly HttpClient _httpClient;
    private readonly IOptions<GmailOptions> _options;
    private readonly TimeProvider _timeProvider;

    public GoogleOAuthClient(HttpClient httpClient, IOptions<GmailOptions> options, TimeProvider timeProvider)
    {
        _httpClient = httpClient;
        _options = options;
        _timeProvider = timeProvider;
    }

    public string CreateAuthorizationUrl(string state, string codeChallenge)
    {
        var settings = GetSettings();
        return QueryHelpers.AddQueryString("https://accounts.google.com/o/oauth2/v2/auth",
            new Dictionary<string, string?>
            {
                ["client_id"] = settings.ClientId, ["redirect_uri"] = settings.RedirectUri,
                ["response_type"] = "code", ["scope"] = "openid email " + ReadOnlyScope + " " + SendScope,
                ["access_type"] = "offline", ["prompt"] = "consent select_account",
                ["state"] = state, ["code_challenge"] = codeChallenge, ["code_challenge_method"] = "S256"
            });
    }

    public async Task<GoogleTokenSet> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken)
    {
        var settings = GetSettings();
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = settings.ClientId, ["client_secret"] = settings.ClientSecret,
                ["redirect_uri"] = settings.RedirectUri, ["grant_type"] = "authorization_code",
                ["code"] = code, ["code_verifier"] = codeVerifier
            })
        };
        var response = await SendAsync<TokenResult>(request, cancellationToken, ErrorCode.OAuthRejected);
        var tokens = ToTokenSet(response);
        if (!tokens.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(ReadOnlyScope))
        {
            throw new AppException(ErrorCode.OAuthDenied, "Gmail read permission was not granted.");
        }
        return tokens;
    }

    public async Task<GoogleMailboxAccount> GetMailboxAccountAsync(string accessToken, CancellationToken cancellationToken)
    {
        GetSettings();
        using var identityRequest = AuthorizedGet("https://openidconnect.googleapis.com/v1/userinfo", accessToken);
        var identity = await SendAsync<IdentityResult>(identityRequest, cancellationToken);
        if (string.IsNullOrWhiteSpace(identity.Subject) || identity.Subject.Length > 255 || !identity.EmailVerified)
        {
            throw new AppException(ErrorCode.OAuthRejected, "Google did not provide a verified account.");
        }

        using var profileRequest = AuthorizedGet("https://gmail.googleapis.com/gmail/v1/users/me/profile", accessToken);
        var profile = await SendAsync<ProfileResult>(profileRequest, cancellationToken);
        if (string.IsNullOrWhiteSpace(profile.Email) || profile.Email.Length > 320
            || !new EmailAddressAttribute().IsValid(profile.Email))
        {
            throw Unavailable();
        }
        return new GoogleMailboxAccount(identity.Subject, profile.Email);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken,
        ErrorCode rejectedGrant = ErrorCode.MailboxReconnectRequired)
    {
        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw new AppException(rejectedGrant, "Google authorization is no longer valid.");
                }
                if (request.RequestUri!.AbsoluteUri == TokenEndpoint && response.StatusCode == HttpStatusCode.BadRequest)
                {
                    var error = await response.Content.ReadFromJsonAsync<ErrorResult>(cancellationToken);
                    if (error?.Error == "invalid_grant")
                    {
                        throw new AppException(rejectedGrant, "Google authorization is no longer valid.");
                    }
                }
                // Provider bodies can contain credentials; only expose our own safe messages.
                throw Unavailable();
            }
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken) ?? throw Unavailable();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException)
        {
            throw Unavailable();
        }
    }

    public async Task<GoogleTokenSet> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var settings = GetSettings();
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = settings.ClientId, ["client_secret"] = settings.ClientSecret,
                ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken
            })
        };
        return ToTokenSet(await SendAsync<TokenResult>(request, cancellationToken));
    }

    private GoogleTokenSet ToTokenSet(TokenResult response)
    {
        if (string.IsNullOrWhiteSpace(response.AccessToken) || response.AccessToken.Length > 16384
            || response.RefreshToken?.Length > 16384 || response.Scope?.Length > 4096
            || !string.Equals(response.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase)
            || response.ExpiresIn is <= 0 or > 86400 || response.RefreshTokenExpiresIn is <= 0)
        {
            throw Unavailable();
        }
        var now = _timeProvider.GetUtcNow();
        return new GoogleTokenSet(response.AccessToken,
            string.IsNullOrWhiteSpace(response.RefreshToken) ? null : response.RefreshToken, now.AddSeconds(response.ExpiresIn),
            response.RefreshTokenExpiresIn is int seconds ? now.AddSeconds(seconds) : null, response.Scope ?? "");
    }

    private GmailOptions GetSettings()
    {
        if (!_options.Value.Enabled)
        {
            throw new AppException(ErrorCode.MailboxNotConfigured, "Configure Gmail OAuth before connecting a mailbox.");
        }
        return _options.Value;
    }

    private static HttpRequestMessage AuthorizedGet(string url, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static AppException Unavailable() => new(ErrorCode.MailboxUnavailable,
        "The mailbox provider is temporarily unavailable.");

    private sealed record TokenResult(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("refresh_token_expires_in")] int? RefreshTokenExpiresIn,
        [property: JsonPropertyName("scope")] string? Scope,
        [property: JsonPropertyName("token_type")] string? TokenType);
    private sealed record IdentityResult(
        [property: JsonPropertyName("sub")] string? Subject,
        [property: JsonPropertyName("email_verified")] bool EmailVerified);
    private sealed record ProfileResult([property: JsonPropertyName("emailAddress")] string? Email);
    private sealed record ErrorResult([property: JsonPropertyName("error")] string? Error);
}
