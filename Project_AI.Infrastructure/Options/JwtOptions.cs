using System.ComponentModel.DataAnnotations;

namespace Project_AI.Infrastructure.Options;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    [Required] public string Issuer { get; set; } = "InboxAgent";
    [Required] public string Audience { get; set; } = "InboxAgent.Web";
    [Required] public string SigningKey { get; set; } = "";
    [Range(1, 30)] public int AccessTokenMinutes { get; set; } = 15;
    [Range(1, 30)] public int RefreshTokenDays { get; set; } = 7;

    public bool HasValidSigningKey()
    {
        try { return Convert.FromBase64String(SigningKey).Length >= 32; }
        catch (FormatException) { return false; }
    }
}
