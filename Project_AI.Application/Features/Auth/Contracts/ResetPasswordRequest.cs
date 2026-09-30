using System.ComponentModel.DataAnnotations;

namespace Project_AI.Application.Features.Auth.Contracts;

public sealed record ResetPasswordRequest(Guid UserId,
    [Required, MaxLength(4096)] string Token,
    [Required, StringLength(128, MinimumLength = 12)] string NewPassword);
