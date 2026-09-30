namespace Project_AI.Application.Features.Auth.Contracts;

public sealed record AuthAccount(UserProfile Profile, string SecurityStamp, bool EmailConfirmed);
