using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Replies;
using Project_AI.Application.Interfaces;

namespace Project_AI.Infrastructure.Services;

public sealed class GmailReplyClient : IGmailReplyClient
{
    private readonly HttpClient _httpClient;
    private readonly GmailMessageClient _messages;
    private readonly MailboxAccessTokenService _tokens;
    public GmailReplyClient(HttpClient httpClient, GmailMessageClient messages, MailboxAccessTokenService tokens)
    {
        _httpClient = httpClient; _messages = messages; _tokens = tokens;
    }
    public Task<ReplyThread> GetThreadAsync(ReplySource source, CancellationToken cancellationToken) =>
        _messages.GetReplyThreadAsync(source, cancellationToken);

    public async Task<ReplySendResult> SendAsync(ReplySendContext context, CancellationToken cancellationToken)
    {
        string token, raw;
        try
        {
            var source = context.Source ?? throw new AppException(ErrorCode.DraftNotEditable, "The reply has no active source.");
            token = await _tokens.GetAccessTokenAsync(source.UserId, source.MailboxId,
                cancellationToken, requireSendPermission: true, expectedMailboxVersion: source.MailboxVersion);
            using var message = GmailReplyFormatter.Create(context);
            using var buffer = new MemoryStream();
            await message.WriteToAsync(buffer, cancellationToken);
            raw = WebEncoders.Base64UrlEncode(buffer.ToArray());
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (AppException exception) { throw new ReplySendException(exception.Code, false); }
        catch (OperationCanceledException) { throw new ReplySendException(ErrorCode.RequestTimeout, false); }
        // No write has been attempted until this point. Once attempted, network/5xx/cancellation is uncertain.
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://gmail.googleapis.com/gmail/v1/users/me/messages/send");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { raw, threadId = context.Draft.ThreadId });
        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new ReplySendException(ErrorCode.MailboxReconnectRequired, false);
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new ReplySendException(ErrorCode.MailboxSendPermissionRequired, false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new ReplySendException(ErrorCode.ReplySendThrottled, false);
            if (response.StatusCode == HttpStatusCode.BadRequest)
                throw new ReplySendException(ErrorCode.InvalidReplyTarget, false);
            if (!response.IsSuccessStatusCode) throw new ReplySendException(ErrorCode.ReplySendUnknown, true);
            using var result = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
            var id = result?.RootElement.GetProperty("id").GetString();
            var threadId = result?.RootElement.GetProperty("threadId").GetString();
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || !id.All(char.IsAsciiLetterOrDigit)
                || threadId != context.Draft.ThreadId) throw new ReplySendException(ErrorCode.ReplySendUnknown, true);
            return new ReplySendResult(id, threadId);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException
            or InvalidOperationException or KeyNotFoundException or IOException)
        {
            throw new ReplySendException(ErrorCode.ReplySendUnknown, true);
        }
    }
}
