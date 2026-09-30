using System.ComponentModel.DataAnnotations;

namespace Project_AI.Infrastructure.Options;

public sealed class EmailOptions
{
    public const string Section = "Email";
    public bool DeliveryEnabled { get; set; }
    public string ApiKey { get; set; } = "";
    public string FromEmail { get; set; } = "";
    [Required] public string FromName { get; set; } = "InboxAgent";
    [Required, Url] public string FrontendBaseUrl { get; set; } = "http://localhost:3000";
}
