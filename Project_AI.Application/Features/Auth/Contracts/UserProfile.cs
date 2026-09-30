namespace Project_AI.Application.Features.Auth.Contracts;

public sealed record UserProfile(Guid Id, string Email, string DisplayName, IReadOnlyList<string> Roles);
