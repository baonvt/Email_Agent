namespace Project_AI.Application.DTOs.Auth;

public sealed record UserProfile(Guid Id, string Email, string DisplayName, IReadOnlyList<string> Roles);
