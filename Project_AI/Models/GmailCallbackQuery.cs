using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace Project_AI.API.Models;

public sealed class GmailCallbackQuery
{
    [Required, StringLength(43, MinimumLength = 43)]
    public string State { get; set; } = "";
    [MaxLength(4096)]
    public string? Code { get; set; }
    [MaxLength(128)]
    public string? Error { get; set; }
    [FromQuery(Name = "iss"), MaxLength(512)]
    public string? Issuer { get; set; }
}
