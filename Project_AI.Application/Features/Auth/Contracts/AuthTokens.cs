namespace Project_AI.Application.Features.Auth.Contracts;

// Internal application result. The API only serializes TokenResponse; refresh stays in an HttpOnly cookie.
public sealed record AuthTokens(string AccessToken, DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken, DateTimeOffset RefreshTokenExpiresAt, UserProfile User);
