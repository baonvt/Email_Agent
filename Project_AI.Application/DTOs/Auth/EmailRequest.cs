using System.ComponentModel.DataAnnotations;

namespace Project_AI.Application.DTOs.Auth;

public sealed record EmailRequest([Required, EmailAddress, MaxLength(254)] string Email);
