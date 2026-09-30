using System.ComponentModel.DataAnnotations;

namespace Project_AI.Application.Features.Auth.Contracts;

public sealed record EmailRequest([Required, EmailAddress, MaxLength(254)] string Email);
