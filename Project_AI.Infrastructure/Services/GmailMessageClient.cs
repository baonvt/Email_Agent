using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Emails;
using Project_AI.Application.DTOs.Replies;
using Project_AI.Application.Interfaces;
using Project_AI.Infrastructure.Models;
using Project_AI.Infrastructure.Options;

namespace Project_AI.Infrastructure.Services;

public sealed class GmailMessageClient : IGmailMessageClient
{
    private const string BaseUrl = "https://gmail.googleapis.com/gmail/v1/users/me/";
    private readonly HttpClient _httpClient;
    private readonly MailboxAccessTokenService _tokens;
    private readonly IOptions<GmailOptions> _options;

    public GmailMessageClient(HttpClient httpClient, MailboxAccessTokenService tokens, IOptions<GmailOptions> options)
    {
        _httpClient = httpClient;
        _tokens = tokens;
        _options = options;
    }

    public async Task<string> GetHistoryIdAsync(Guid userId, Guid mailboxId, CancellationToken cancellationToken)
    {
        var profile = await GetAsync<GmailProfileResult>(userId, mailboxId, "profile", false, cancellationToken);
        return ValidHistory(profile!.HistoryId);
    }

    public async Task<GmailMessagePage> ListInboxAsync(Guid userId, Guid mailboxId, int limit,
        string? pageToken, CancellationToken cancellationToken)
    {
        var path = QueryHelpers.AddQueryString("messages", new Dictionary<string, string?>
        {
            ["labelIds"] = "INBOX", ["maxResults"] = Math.Clamp(limit, 1, 100).ToString(), ["pageToken"] = pageToken
        });
        var page = await GetAsync<GmailListResult>(userId, mailboxId, path, false, cancellationToken);
        return new GmailMessagePage((page!.Messages ?? []).Select(x => ValidMessageId(x.Id)).Distinct().ToArray(),
            ValidPageToken(page.NextPageToken));
    }

    public async Task<ProviderEmail?> GetMessageAsync(Guid userId, Guid mailboxId, string messageId,
        CancellationToken cancellationToken)
    {
        var path = "messages/" + Uri.EscapeDataString(ValidMessageId(messageId)) + "?format=full";
        var message = await GetAsync<GmailMessageResult>(userId, mailboxId, path, true, cancellationToken);
        if (message is null) return null;
        try
        {
            if (message.Id != messageId) throw Unavailable();
            return GmailMessageParser.Parse(message);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or NotSupportedException)
        {
            throw Unavailable();
        }
    }

    public async Task<GmailHistoryPage> ListHistoryAsync(Guid userId, Guid mailboxId, string historyId,
        string? pageToken, CancellationToken cancellationToken)
    {
        var path = QueryHelpers.AddQueryString("history", new Dictionary<string, string?>
        {
            ["startHistoryId"] = ValidHistory(historyId), ["maxResults"] = "100", ["pageToken"] = pageToken
        });
        var page = await GetAsync<GmailHistoryResult>(userId, mailboxId, path, false, cancellationToken);
        // Include archive/removal events; filtering history by INBOX can miss them.
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in page!.History ?? [])
        {
            foreach (var message in record.Messages ?? []) ids.Add(ValidMessageId(message.Id));
            foreach (var change in (record.MessagesAdded ?? []).Concat(record.MessagesDeleted ?? [])
                .Concat(record.LabelsAdded ?? []).Concat(record.LabelsRemoved ?? []))
                ids.Add(ValidMessageId(change.Message.Id));
        }
        return new GmailHistoryPage(ids.ToArray(), ValidHistory(page.HistoryId), ValidPageToken(page.NextPageToken));
    }

    public async Task<ReplyThread> GetReplyThreadAsync(ReplySource source, CancellationToken cancellationToken)
    {
        var path = "threads/" + Uri.EscapeDataString(ValidMessageId(source.Email.ThreadId)) + "?format=full";
        var thread = await GetAsync<GmailThreadResult>(source.UserId, source.MailboxId, path, true, cancellationToken);
        if (thread is null) throw new AppException(ErrorCode.DraftOutdated, "The conversation was removed.");
        try { return GmailReplyFormatter.ParseThread(thread, source); }
        catch (Exception exception) when (exception is FormatException or ArgumentException or NotSupportedException)
        {
            throw Unavailable();
        }
    }

    private async Task<T?> GetAsync<T>(Guid userId, Guid mailboxId, string path, bool missingMessageAllowed,
        CancellationToken cancellationToken) where T : class
    {
        if (!_options.Value.Enabled)
            throw new AppException(ErrorCode.MailboxNotConfigured, "Enable Gmail OAuth before synchronizing.");
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var token = await _tokens.GetAccessTokenAsync(userId, mailboxId, cancellationToken, attempt == 1);
                using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    if (attempt == 0) continue;
                    await _tokens.RejectAccessTokenAsync(userId, mailboxId, token, cancellationToken);
                    throw new AppException(ErrorCode.MailboxReconnectRequired, "Google authorization is no longer valid.");
                }
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    if (missingMessageAllowed) return null;
                    if (path.StartsWith("history?", StringComparison.Ordinal))
                        throw new AppException(ErrorCode.MailboxHistoryExpired, "The Gmail history cursor expired.");
                }
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    throw new AppException(ErrorCode.MailboxSyncThrottled, "Google temporarily limited synchronization.");
                if (!response.IsSuccessStatusCode) throw Unavailable();
                return await response.Content.ReadFromJsonAsync<T>(cancellationToken) ?? throw Unavailable();
            }
            throw Unavailable();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException)
        {
            throw Unavailable(); // Never expose provider bodies, message content or tokens.
        }
    }

    private static string ValidMessageId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128
        && value.All(char.IsAsciiLetterOrDigit) ? value : throw Unavailable();
    private static string ValidHistory(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 32
        && value.All(char.IsAsciiDigit) ? value : throw Unavailable();
    private static string? ValidPageToken(string? value) => value?.Length > 4096 ? throw Unavailable() : value;
    private static AppException Unavailable() => new(ErrorCode.MailboxUnavailable, "Gmail synchronization is temporarily unavailable.");
}
