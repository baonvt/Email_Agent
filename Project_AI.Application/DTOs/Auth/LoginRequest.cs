using System.ComponentModel.DataAnnotations;

namespace Project_AI.Application.DTOs.Auth;

public sealed record LoginRequest(
    [Required, EmailAddress, MaxLength(254)] string Email,
    [Required, StringLength(128, MinimumLength = 1)] string Password);
