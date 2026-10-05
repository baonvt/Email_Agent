namespace Project_AI.Application.DTOs.Auth;

public sealed record AuthAccount(UserProfile Profile, string SecurityStamp, bool EmailConfirmed);
