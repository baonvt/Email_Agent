using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;


using Project_AI.Infrastructure.Persistence;

namespace Project_AI.Infrastructure.Email;

public sealed class SendGridTransport(HttpClient http, IOptions<EmailOptions> options)
{
    public async Task SendAsync(EmailPayload message, CancellationToken ct)
    {
        var settings = options.Value;
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
        using var response = await http.SendAsync(request, ct);
        // Never log provider bodies, API keys, recipients or confirmation/reset URLs.
        if (response.StatusCode != HttpStatusCode.Accepted)
            throw new HttpRequestException("SendGrid did not accept the email.", null, response.StatusCode);
    }
}
