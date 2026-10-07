using System.Security.Cryptography;
using System.Text.Json;
using MimeKit;
using MimeKit.Utils;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Replies;
using Project_AI.Infrastructure.Models;

namespace Project_AI.Infrastructure.Services;

internal static class GmailReplyFormatter
{
    public static ReplyThread ParseThread(GmailThreadResult thread, ReplySource source)
    {
        if (thread.Id != source.Email.ThreadId || thread.Messages is not { Length: > 0 and <= 200 }
            || thread.Messages.Any(x => x.ThreadId != thread.Id || !ValidId(x.Id))
            || thread.Messages.Select(x => x.Id).Distinct().Count() != thread.Messages.Length)
            throw new AppException(ErrorCode.MailboxUnavailable, "Gmail returned an invalid conversation.");
        var anchor = thread.Messages.SingleOrDefault(x => x.Id == source.Email.MessageId)
            ?? throw new AppException(ErrorCode.DraftOutdated, "The source email was removed from the conversation.");
        var messages = thread.Messages.Select(GmailMessageParser.Parse).OrderBy(x => x.ReceivedAt)
            .ThenBy(x => x.MessageId, StringComparer.Ordinal).ToArray();
        var parsed = messages.Single(x => x.MessageId == anchor.Id);
        var sender = Address(parsed.From);
        if (sender.Equals(source.MailboxEmail, StringComparison.OrdinalIgnoreCase)) throw InvalidTarget();
        var replyTo = Header(anchor, "Reply-To");
        var target = Address(string.IsNullOrWhiteSpace(replyTo) ? parsed.From : replyTo);
        if (target.Equals(source.MailboxEmail, StringComparison.OrdinalIgnoreCase)) throw InvalidTarget();
        var messageIds = MimeUtils.EnumerateReferences(Header(anchor, "Message-ID")).ToArray();
        if (messageIds.Length != 1 || !ValidReference(messageIds[0])) throw InvalidTarget();
        var references = MimeUtils.EnumerateReferences(Header(anchor, "References")).ToArray();
        if (references.Any(x => !ValidReference(x))) throw InvalidTarget();
        references = references.TakeLast(49).Append(messageIds[0]).Distinct().ToArray();
        // Labels are excluded; every message's content and relevant reply headers are included.
        var fingerprintInput = messages.Select(x => new { x.MessageId, x.ThreadId, x.Subject, x.From, x.To,
            x.Snippet, x.BodyText, x.BodyTruncated, x.ReceivedAt }).ToArray();
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            messages = fingerprintInput, target, messageId = messageIds[0], references
        })));
        var selected = messages.TakeLast(20).ToArray();
        // Always include the email being replied to, even if it is older than the recent context window.
        if (!selected.Any(x => x.MessageId == parsed.MessageId)) selected = selected.Skip(1).Prepend(parsed).ToArray();
        return new ReplyThread(parsed, target, messageIds[0], references, fingerprint, selected,
            messages.Length > selected.Length || selected.Any(x => x.BodyTruncated));
    }

    public static MimeMessage Create(ReplySendContext context)
    {
        var draft = context.Draft;
        if (!ValidReference(draft.InReplyTo) || draft.References.Any(x => !ValidReference(x))
            || draft.Subject.Length > 2000 || draft.Subject.Any(c => c is '\r' or '\n' or '\0')) throw InvalidTarget();
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("", Address(context.Source.MailboxEmail)));
        message.To.Add(new MailboxAddress("", Address(draft.To)));
        message.Subject = draft.Subject; // Gmail thread replies require the matching subject.
        message.MessageId = draft.OutgoingMessageId;
        message.InReplyTo = draft.InReplyTo;
        foreach (var reference in draft.References) message.References.Add(reference);
        message.Body = new TextPart("plain") { Text = draft.BodyText };
        return message;
    }

    private static string Header(GmailMessageResult message, string name)
    {
        var headers = message.Payload?.Headers?.Where(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
        if (headers.Length > 1) throw InvalidTarget();
        var value = headers.FirstOrDefault()?.Value ?? "";
        if (value.Length > 8000 || value.Any(c => c is '\r' or '\n' or '\0')) throw InvalidTarget();
        return value;
    }
    private static string Address(string value)
    {
        if (value.Length > 4000 || value.Any(c => c is '\r' or '\n' or '\0')
            || !MailboxAddress.TryParse(value, out var mailbox) || mailbox.Address.Length > 320
            || !mailbox.Address.Contains('@') || !mailbox.Address.All(c => c < 128 && !char.IsControl(c))) throw InvalidTarget();
        return mailbox.Address;
    }
    private static bool ValidId(string value) => value.Length is > 0 and <= 128 && value.All(char.IsAsciiLetterOrDigit);
    private static bool ValidReference(string value) => value.Length is > 0 and <= 1000
        && value.Contains('@') && value.All(c => c > 32 && c < 127 && c is not ('<' or '>'));
    private static AppException InvalidTarget() => new(ErrorCode.InvalidReplyTarget, "The reply target or headers are invalid.");
}
