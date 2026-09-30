using System.ComponentModel.DataAnnotations;

namespace Project_AI.Application.Features.Auth.Contracts;

public sealed record ConfirmEmailRequest(Guid UserId, [Required, MaxLength(4096)] string Token);
