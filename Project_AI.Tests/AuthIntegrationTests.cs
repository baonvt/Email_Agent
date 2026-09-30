using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Project_AI.Infrastructure.Email;
using Project_AI.Infrastructure.Persistence;
using StackExchange.Redis;
using Xunit;

namespace Project_AI.Tests;

public sealed class AuthIntegrationTests(AuthFixture fixture) : IClassFixture<AuthFixture>
{
    private const string Password = "Testing-Inbox!2026";
    private const string CookieName = "inboxagent.refresh";

    private HttpClient Client() => ConfigureClient(fixture.Factory.CreateClient(
        new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false }));

    private static HttpClient ConfigureClient(HttpClient client)
    {
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:3000");
        client.DefaultRequestHeaders.Add("X-InboxAgent-CSRF", "1");
        return client;
    }

    private async Task<string> RegisterAsync(HttpClient client)
    {
        var email = Guid.NewGuid().ToString("N") + "@example.test";
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email, password = Password, displayName = "Test User", role = "Admin", emailConfirmed = true
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return email;
    }

    private async Task<ConfirmEmailRequest> ReadEmailAsync(string email, string subject = "Confirm your InboxAgent email")
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var protection = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("InboxAgent.AuthEmail.v1");
        foreach (var row in await db.EmailOutbox.AsNoTracking().OrderByDescending(x => x.CreatedAt).ToListAsync())
        {
            if (row.ProtectedPayload == "") continue;
            Assert.DoesNotContain(email, row.ProtectedPayload);
            var payload = JsonSerializer.Deserialize<EmailPayload>(protection.Unprotect(row.ProtectedPayload))!;
            if (payload.To != email || payload.Subject != subject) continue;
            var link = payload.Text.Split('\n')[1];
            var query = QueryHelpers.ParseQuery(new Uri(link).Query);
            return new ConfirmEmailRequest(Guid.Parse(query["userId"].ToString()), query["token"].ToString());
        }
        throw new InvalidOperationException("Expected auth email was not queued.");
    }

    private async Task<string> ConfirmedUserAsync(HttpClient client)
    {
        var email = await RegisterAsync(client);
        var response = await client.PostAsJsonAsync("/api/auth/confirm-email", await ReadEmailAsync(email));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return email;
    }

    private async Task<(TokenResponse Token, string Refresh)> LoginAsync(HttpClient client, string email, string password = Password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("refreshToken", json);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith(CookieName + "="));
        Assert.Contains("httponly", cookie.ToLowerInvariant());
        Assert.Contains("samesite=lax", cookie.ToLowerInvariant());
        return ((await response.Content.ReadFromJsonAsync<TokenResponse>())!, cookie.Split(';')[0].Split('=', 2)[1]);
    }

    private static async Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refresh)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", CookieName + "=" + refresh);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string access, string path = "/api/auth/me")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> LogoutAsync(HttpClient client, string access, bool all = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, all ? "/api/auth/logout-all" : "/api/auth/logout");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Registration_requires_confirmation_and_cannot_assign_admin()
    {
        using var client = Client();
        var email = await RegisterAsync(client);
        var unconfirmed = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        Assert.Equal(HttpStatusCode.Unauthorized, unconfirmed.StatusCode);
        var link = await ReadEmailAsync(email);
        var wrong = await client.PostAsJsonAsync("/api/auth/confirm-email", link with { Token = "invalid" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/confirm-email", link)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/confirm-email", link)).StatusCode);
        var login = await LoginAsync(client, email);
        Assert.Equal(new[] { "User" }, login.Token.User.Roles);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, login.Token.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(client, login.Token.AccessToken, "/api/admin/status")).StatusCode);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var user = await db.Users.SingleAsync(x => x.Email == email);
        Assert.NotEqual(Password, user.PasswordHash);
        Assert.All(await db.RefreshTokens.Where(x => x.SessionId == new JwtSecurityTokenHandler()
            .ReadJwtToken(login.Token.AccessToken).Claims.Where(c => c.Type == "sid").Select(c => Guid.Parse(c.Value)).Single())
            .ToListAsync(), token => Assert.NotEqual(login.Refresh, token.TokenHash));
    }

    [Fact]
    public async Task Duplicate_registration_and_unknown_email_recovery_have_generic_responses()
    {
        using var client = Client();
        var email = await ConfirmedUserAsync(client);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, Password, "Other name"))).StatusCode);
        var known = await client.PostAsJsonAsync("/api/auth/forgot-password", new EmailRequest(email));
        var unknown = await client.PostAsJsonAsync("/api/auth/forgot-password", new EmailRequest("missing@example.test"));
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/resend-confirmation",
            new EmailRequest("missing@example.test"))).StatusCode);
    }

    [Fact]
    public async Task Password_reset_is_single_use_and_revokes_all_devices()
    {
        using var client = Client();
        var email = await ConfirmedUserAsync(client);
        var first = await LoginAsync(client, email);
        var second = await LoginAsync(client, email);
        await client.PostAsJsonAsync("/api/auth/forgot-password", new EmailRequest(email));
        var link = await ReadEmailAsync(email, "Reset your InboxAgent password");
        var reset = new ResetPasswordRequest(link.UserId, link.Token, "New-Inbox!Password2026");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/reset-password", reset with { Token = "invalid" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/reset-password", reset)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/reset-password", reset)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(client, first.Token.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(client, second.Token.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, first.Refresh)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password))).StatusCode);
        await LoginAsync(client, email, reset.NewPassword);
    }

    [Fact]
    public async Task Refresh_rotation_and_replay_revoke_the_entire_family()
    {
        using var client = Client();
        var login = await LoginAsync(client, await ConfirmedUserAsync(client));
        var refreshed = await RefreshAsync(client, login.Refresh);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var newToken = (await refreshed.Content.ReadFromJsonAsync<TokenResponse>())!;
        var newRefresh = refreshed.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith(CookieName + "="))
            .Split(';')[0].Split('=', 2)[1];
        Assert.NotEqual(login.Refresh, newRefresh);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, login.Refresh)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, newRefresh)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(client, newToken.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(client, login.Token.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_refresh_cannot_issue_two_replacements()
    {
        using var client = Client();
        var login = await LoginAsync(client, await ConfirmedUserAsync(client));
        var responses = await Task.WhenAll(RefreshAsync(client, login.Refresh), RefreshAsync(client, login.Refresh));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Unauthorized);
        var success = (await responses.Single(x => x.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(client, success.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task Logout_blacklists_access_token_and_preserves_other_devices()
    {
        using var client = Client();
        var email = await ConfirmedUserAsync(client);
        var first = await LoginAsync(client, email);
        var other = await LoginAsync(client, email);
        Assert.Equal(HttpStatusCode.NoContent, (await LogoutAsync(client, first.Token.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(client, first.Token.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, first.Refresh)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, other.Token.AccessToken)).StatusCode);
        var redis = fixture.Factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        var jti = new JwtSecurityTokenHandler().ReadJwtToken(first.Token.AccessToken).Claims.Single(x => x.Type == "jti").Value;
        var ttl = await redis.KeyTimeToLiveAsync("inboxagent:auth:revoked:" + jti);
        Assert.True(ttl > TimeSpan.Zero && ttl <= TimeSpan.FromMinutes(15));
        await redis.KeyDeleteAsync("inboxagent:auth:revoked:" + jti);
        // PostgreSQL session revocation remains authoritative even if Redis loses a key.
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(client, first.Token.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task Logout_all_invalidates_every_session()
    {
        using var client = Client();
        var email = await ConfirmedUserAsync(client);
        var first = await LoginAsync(client, email);
        var second = await LoginAsync(client, email);
        Assert.Equal(HttpStatusCode.NoContent, (await LogoutAsync(client, first.Token.AccessToken, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(client, second.Token.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, second.Refresh)).StatusCode);
        await LoginAsync(client, email);
    }

    [Fact]
    public async Task Promotion_requires_verified_user_and_invalidates_old_role_claims()
    {
        using var client = Client();
        var email = await ConfirmedUserAsync(client);
        var old = await LoginAsync(client, email);
        await AuthDatabaseInitializer.PromoteAdminAsync(fixture.Factory.Services, email);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(client, old.Token.AccessToken)).StatusCode);
        var admin = await LoginAsync(client, email);
        Assert.Contains("Admin", admin.Token.User.Roles);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, admin.Token.AccessToken, "/api/admin/status")).StatusCode);
    }

    [Fact]
    public async Task Missing_csrf_header_and_untrusted_origins_are_rejected()
    {
        using var client = Client();
        client.DefaultRequestHeaders.Remove("X-InboxAgent-CSRF");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("test@example.test", Password))).StatusCode);
        client.DefaultRequestHeaders.Add("X-InboxAgent-CSRF", "1");
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:3000.evil.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task Failed_logins_lock_the_account_after_five_attempts()
    {
        using var client = Client();
        var email = await ConfirmedUserAsync(client);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Wrong-password!123"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password))).StatusCode);
    }

    [Fact]
    public async Task Tampered_and_expired_jwts_and_missing_refresh_are_rejected()
    {
        using var client = Client();
        var login = await LoginAsync(client, await ConfirmedUserAsync(client));
        var parts = login.Token.AccessToken.Split('.');
        var json = System.Text.Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(parts[1]));
        parts[1] = WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(json.Replace("\"User\"", "\"Admin\"")));
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(client, string.Join('.', parts))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/auth/refresh", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Redis_outage_fails_closed_for_protected_requests_and_login()
    {
        using var client = Client();
        var email = await ConfirmedUserAsync(client);
        var login = await LoginAsync(client, email);
        // A separate host connects to an unused Redis port; the real shared Redis is untouched.
        await using var offline = new AuthFactory(fixture.Postgres, "127.0.0.1:1,abortConnect=false,connectTimeout=100,asyncTimeout=100,connectRetry=0");
        using var offlineClient = ConfigureClient(offline.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false }));
        // The offline factory has another signing key; sign in through its app service to reach Redis.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await offlineClient.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password))).StatusCode);
        using var scope = offline.Services.CreateScope();
        var context = Project_AI.API.Authentication.AccessTokenClaims.Read(new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(new JwtSecurityTokenHandler().ReadJwtToken(login.Token.AccessToken).Claims)))!;
        var ex = await Assert.ThrowsAsync<AuthException>(() => scope.ServiceProvider.GetRequiredService<IAuthSessions>()
            .ValidateAsync(context, CancellationToken.None));
        Assert.Equal(AuthErrorKind.Unavailable, ex.Kind);
    }

    [Fact]
    public async Task Auth_requests_are_rate_limited()
    {
        await using var limited = new AuthFactory(fixture.Postgres, fixture.Redis, 2);
        using var client = ConfigureClient(limited.CreateClient());
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/forgot-password", new EmailRequest("missing@example.test"))).StatusCode);
        var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new EmailRequest("missing@example.test"));
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("60", response.Headers.GetValues("Retry-After").Single());
    }
}
