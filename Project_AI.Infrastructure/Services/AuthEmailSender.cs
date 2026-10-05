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

public sealed class AuthEmailSender : IAuthEmailSender
{
    private readonly AuthDbContext _dbContext;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IOptions<EmailOptions> _emailOptions;
    private readonly TimeProvider _timeProvider;

    public AuthEmailSender(
        AuthDbContext dbContext,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<EmailOptions> emailOptions,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _dataProtectionProvider = dataProtectionProvider;
        _emailOptions = emailOptions;
        _timeProvider = timeProvider;
    }

    public Task QueueConfirmationAsync(AuthAccount account, string token, CancellationToken cancellationToken) =>
        QueueAsync(account, token, "/auth/confirm-email", "Confirm your InboxAgent email", cancellationToken);

    public Task QueuePasswordResetAsync(AuthAccount account, string token, CancellationToken cancellationToken) =>
        QueueAsync(account, token, "/auth/reset-password", "Reset your InboxAgent password", cancellationToken);

    private async Task QueueAsync(AuthAccount account, string token, string path, string subject, CancellationToken cancellationToken)
    {
        var link = QueryHelpers.AddQueryString(_emailOptions.Value.FrontendBaseUrl.TrimEnd('/') + path,
            new Dictionary<string, string?> { ["userId"] = account.Profile.Id.ToString(), ["token"] = token });
        var html = $"<p>{WebUtility.HtmlEncode(subject)}</p><p><a href=\"{WebUtility.HtmlEncode(link)}\">Continue</a></p>"
            + "<p>If you did not request this, you can ignore this email.</p>";
        var payload = new EmailPayload(account.Profile.Email, subject, html,
            $"{subject}\n{link}\nIf you did not request this, you can ignore this email.");
        var now = _timeProvider.GetUtcNow();
        _dbContext.EmailOutbox.Add(new EmailOutboxMessage
        {
            ProtectedPayload = _dataProtectionProvider.CreateProtector("InboxAgent.AuthEmail.v1")
                .Protect(JsonSerializer.Serialize(payload)),
            CreatedAt = now, NextAttemptAt = now
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
