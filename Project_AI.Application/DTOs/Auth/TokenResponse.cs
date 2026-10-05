namespace Project_AI.Application.DTOs.Auth;

public sealed record TokenResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, UserProfile User);
