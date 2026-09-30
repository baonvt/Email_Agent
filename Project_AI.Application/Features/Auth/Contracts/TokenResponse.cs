namespace Project_AI.Application.Features.Auth.Contracts;

public sealed record TokenResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, UserProfile User);
