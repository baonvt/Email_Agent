namespace Project_AI.API.Options;

public sealed class AuthWebOptions
{
    public const string Section = "AuthWeb";
    public string[] AllowedOrigins { get; set; } = ["http://localhost:3000"];
    public bool AllowInsecureCookiesForDevelopment { get; set; }
    public string SameSite { get; set; } = "Lax";
    public string CookieName => AllowInsecureCookiesForDevelopment ? "inboxagent.refresh" : "__Host-inboxagent.refresh";
}
