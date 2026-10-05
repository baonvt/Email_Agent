using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Project_AI.Infrastructure.Models;
using Project_AI.Infrastructure.Options;

namespace Project_AI.Infrastructure.Services;

public sealed class SendGridTransport
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<EmailOptions> _emailOptions;

    public SendGridTransport(HttpClient httpClient, IOptions<EmailOptions> emailOptions)
    {
        _httpClient = httpClient;
        _emailOptions = emailOptions;
    }

    public async Task SendAsync(EmailPayload message, CancellationToken cancellationToken)
    {
        var settings = _emailOptions.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, "v3/mail/send");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        request.Content = JsonContent.Create(new
        {
            personalizations = new[] { new { to = new[] { new { email = message.To } } } },
            from = new { email = settings.FromEmail, name = settings.FromName },
            subject = message.Subject,
            content = new[]
            {
                new { type = "text/plain", value = message.Text },
                new { type = "text/html", value = message.Html }
            },
            tracking_settings = new
            {
                click_tracking = new { enable = false, enable_text = false },
                open_tracking = new { enable = false }
            }
        });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        // Never log provider bodies, API keys, recipients or confirmation/reset URLs.
        if (response.StatusCode != HttpStatusCode.Accepted)
        {
            throw new HttpRequestException("SendGrid did not accept the email.", null, response.StatusCode);
        }
    }
}
