namespace Project_AI.Infrastructure.Options;

public sealed class GmailOptions
{
    public const string Section = "Gmail";
    public bool Enabled { get; set; }
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string RedirectUri { get; set; } = "http://localhost:8080/api/mailboxes/gmail/callback";

    public bool HasValidRedirectUri(bool isDevelopment)
    {
        return Uri.TryCreate(RedirectUri, UriKind.Absolute, out var uri)
            && (uri.Scheme == "https" || (isDevelopment && uri.Scheme == "http" && uri.IsLoopback))
            && uri.AbsolutePath == "/api/mailboxes/gmail/callback"
            && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }
}
