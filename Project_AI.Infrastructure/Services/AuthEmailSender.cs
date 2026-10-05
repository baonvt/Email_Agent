using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Project_AI.Application.DTOs.Auth;
using Project_AI.Application.Interfaces;
using Project_AI.Infrastructure.Data;
using Project_AI.Infrastructure.Data.Entities;
using Project_AI.Infrastructure.Models;
using Project_AI.Infrastructure.Options;

namespace Project_AI.Infrastructure.Services;

public sealed class AuthEmailSender(AuthDbContext db, IDataProtectionProvider protection,
    IOptions<EmailOptions> options, TimeProvider clock) : IAuthEmailSender
{
    public Task QueueConfirmationAsync(AuthAccount account, string token, CancellationToken ct) =>
        QueueAsync(account, token, "/auth/confirm-email", "Confirm your InboxAgent email", ct);

    public Task QueuePasswordResetAsync(AuthAccount account, string token, CancellationToken ct) =>
        QueueAsync(account, token, "/auth/reset-password", "Reset your InboxAgent password", ct);

    private async Task QueueAsync(AuthAccount account, string token, string path, string subject, CancellationToken ct)
    {
        var link = QueryHelpers.AddQueryString(options.Value.FrontendBaseUrl.TrimEnd('/') + path,
            new Dictionary<string, string?> { ["userId"] = account.Profile.Id.ToString(), ["token"] = token });
        var html = $"<p>{WebUtility.HtmlEncode(subject)}</p><p><a href=\"{WebUtility.HtmlEncode(link)}\">Continue</a></p>"
            + "<p>If you did not request this, you can ignore this email.</p>";
        var payload = new EmailPayload(account.Profile.Email, subject, html,
            $"{subject}\n{link}\nIf you did not request this, you can ignore this email.");
        var now = clock.GetUtcNow();
        db.EmailOutbox.Add(new EmailOutboxMessage
        {
            ProtectedPayload = protection.CreateProtector("InboxAgent.AuthEmail.v1")
                .Protect(JsonSerializer.Serialize(payload)),
            CreatedAt = now, NextAttemptAt = now
        });
        await db.SaveChangesAsync(ct);
    }
}
