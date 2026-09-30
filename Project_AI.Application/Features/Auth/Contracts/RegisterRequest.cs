using System.ComponentModel.DataAnnotations;

namespace Project_AI.Application.Features.Auth.Contracts;

public sealed record RegisterRequest(
    [Required, EmailAddress, MaxLength(254)] string Email,
    [Required, StringLength(128, MinimumLength = 12)] string Password,
    [Required, StringLength(100, MinimumLength = 1)] string DisplayName);
