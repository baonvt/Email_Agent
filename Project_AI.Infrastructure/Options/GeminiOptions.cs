using System.ComponentModel.DataAnnotations;

namespace Project_AI.Infrastructure.Options;

public sealed class GeminiOptions
{
    public const string Section = "Gemini";
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gemini-3.5-flash-lite";
    [Range(1000, 30000)]
    public int MaxInputCharacters { get; set; } = 12000;

    public bool HasValidCredentials() => !string.IsNullOrWhiteSpace(ApiKey) && ApiKey.Length <= 256
        && ApiKey.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
        && Model.Length <= 100 && Model.StartsWith("gemini-", StringComparison.Ordinal)
        && Model.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}
