using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Mailboxes;
using Project_AI.Domain.Entities;

namespace Project_AI.Infrastructure.Services;

public sealed class MailboxTokenProtector
{
    private readonly IDataProtectionProvider _protectionProvider;

    public MailboxTokenProtector(IDataProtectionProvider protectionProvider)
    {
        _protectionProvider = protectionProvider;
    }

    public string Protect(MailboxConnection mailbox, GoogleTokenSet tokens) =>
        CreateProtector(mailbox).Protect(JsonSerializer.Serialize(tokens));

    public GoogleTokenSet Unprotect(MailboxConnection mailbox, string protectedPayload)
    {
        try
        {
            return JsonSerializer.Deserialize<GoogleTokenSet>(CreateProtector(mailbox).Unprotect(protectedPayload))
                ?? throw new JsonException();
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            throw new AppException(ErrorCode.MailboxReconnectRequired, "Reconnect the mailbox to restore its credentials.");
        }
    }

    private IDataProtector CreateProtector(MailboxConnection mailbox) =>
        _protectionProvider.CreateProtector("InboxAgent.MailboxCredentials.v1",
            mailbox.UserId.ToString("N"), mailbox.Id.ToString("N"));
}
