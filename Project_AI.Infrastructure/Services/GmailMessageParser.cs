using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using MimeKit;
using MimeKit.Text;
using MimeKit.Utils;
using Project_AI.Application.DTOs.Emails;
using Project_AI.Infrastructure.Models;

namespace Project_AI.Infrastructure.Services;

internal static class GmailMessageParser
{
    private const int BodyLimit = 100_000;

    public static ProviderEmail Parse(GmailMessageResult message)
    {
        if (string.IsNullOrWhiteSpace(message.Id) || message.Id.Length > 128
            || string.IsNullOrWhiteSpace(message.ThreadId) || message.ThreadId.Length > 128
            || !long.TryParse(message.InternalDate, NumberStyles.None, CultureInfo.InvariantCulture, out var date))
        {
            throw new FormatException("Invalid Gmail message metadata.");
        }
        var plain = new StringBuilder();
        var html = new StringBuilder();
        var truncated = false;
        var attachments = false;
        ReadParts(message.Payload, plain, html, ref truncated, ref attachments, 0);
        var body = plain.Length > 0 ? plain.ToString() : HtmlText(html.ToString());
        if (body.Length == 0 && !string.IsNullOrEmpty(message.Snippet))
        {
            body = message.Snippet;
            truncated = true;
        }
        truncated |= body.Length > BodyLimit;
        return new ProviderEmail(message.Id, message.ThreadId,
            Header(message.Payload, "Subject", 2000), Header(message.Payload, "From", 4000),
            Header(message.Payload, "To", 8000), Clip(message.Snippet ?? "", 2000), Clip(body, BodyLimit),
            truncated, attachments, (message.LabelIds ?? []).Select(x => Clip(x, 128)).Distinct().ToArray(),
            DateTimeOffset.FromUnixTimeMilliseconds(date));
    }

    private static void ReadParts(GmailPart? part, StringBuilder plain, StringBuilder html,
        ref bool truncated, ref bool attachments, int depth)
    {
        if (part is null) return;
        if (depth > 20) { truncated = true; return; }
        if (!string.IsNullOrEmpty(part.Filename)) { attachments = true; return; }
        var mime = part.MimeType?.ToLowerInvariant();
        if (mime is "text/plain" or "text/html")
        {
            if (part.Body?.Data is string data)
            {
                var target = mime == "text/plain" ? plain : html;
                if (data.Length > 2_800_000 || target.Length >= BodyLimit) { truncated = true; return; }
                var bytes = WebEncoders.Base64UrlDecode(data);
                using var stream = new MemoryStream(bytes);
                using var text = new TextPart(mime == "text/plain" ? "plain" : "html");
                var contentType = part.Headers?.FirstOrDefault(x => x.Name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))?.Value;
                if (ContentType.TryParse(contentType ?? mime, out var parsed)) text.ContentType.Charset = parsed.Charset;
                text.Content = new MimeContent(stream);
                var decoded = text.Text;
                truncated |= decoded.Length + target.Length > BodyLimit;
                target.Append(Clip(decoded, BodyLimit - target.Length));
                if (target.Length < BodyLimit) target.AppendLine();
            }
            else if (part.Body?.AttachmentId is not null) truncated = true;
        }
        foreach (var child in part.Parts ?? [])
            ReadParts(child, plain, html, ref truncated, ref attachments, depth + 1);
    }

    private static string Header(GmailPart? part, string name, int limit)
    {
        var value = part?.Headers?.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value ?? "";
        return Clip(Rfc2047.DecodeText(Encoding.UTF8.GetBytes(value)), limit);
    }

    private static string HtmlText(string html)
    {
        using var reader = new StringReader(html);
        var tokenizer = new HtmlTokenizer(reader) { DecodeCharacterReferences = true };
        var output = new StringBuilder();
        string? suppressed = null;
        while (tokenizer.ReadNextToken(out var token))
        {
            if (token is HtmlTagToken tag)
            {
                var name = tag.Name.ToLowerInvariant();
                if (name is "script" or "style" or "head")
                {
                    if (!tag.IsEndTag && suppressed is null) suppressed = name;
                    else if (tag.IsEndTag && suppressed == name) suppressed = null;
                }
                if (suppressed is null && name is ("br" or "p" or "div" or "li" or "tr")) output.AppendLine();
            }
            else if (suppressed is null && token is HtmlDataToken data && token.Kind == HtmlTokenKind.Data)
                output.Append(data.Data);
        }
        return output.ToString().Trim();
    }

    private static string Clip(string value, int limit)
    {
        value = value.Replace("\0", ""); // PostgreSQL text cannot contain NUL.
        if (value.Length <= limit) return value;
        if (limit > 0 && char.IsHighSurrogate(value[limit - 1])) limit--;
        return value[..limit];
    }
}
