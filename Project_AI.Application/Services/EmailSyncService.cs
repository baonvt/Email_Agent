using System.Text.Json;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Emails;
using Project_AI.Application.Interfaces;

namespace Project_AI.Application.Services;

public sealed class EmailSyncService : IEmailSyncService
{
    private const int InitialMessageLimit = 100;
    private readonly IEmailSyncStore _store;
    private readonly IGmailMessageClient _gmail;

    public EmailSyncService(IEmailSyncStore store, IGmailMessageClient gmail)
    {
        _store = store;
        _gmail = gmail;
    }

    public async Task<MailboxSyncResult> SyncAsync(Guid userId, Guid mailboxId, CancellationToken cancellationToken)
    {
        var context = await _store.BeginAsync(userId, mailboxId, cancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            if (context.HistoryId is null) return await SnapshotAsync(context, deadline.Token);
            try { return await IncrementalAsync(context, deadline.Token); }
            catch (AppException exception) when (exception.Code == ErrorCode.MailboxHistoryExpired)
            {
                return await SnapshotAsync(context, deadline.Token);
            }
        }
        catch (Exception exception)
        {
            var code = exception is AppException app ? app.Code
                : exception is OperationCanceledException ? ErrorCode.RequestTimeout : ErrorCode.InternalError;
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _store.FailAsync(context, JsonNamingPolicy.SnakeCaseLower.ConvertName(code.ToString()), cleanup.Token);
            }
            catch (Exception)
            {
                // Preserve the original failure; the lease expires if database cleanup is unavailable.
            }
            if (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
                throw new AppException(ErrorCode.RequestTimeout, "Synchronization timed out. Try again.");
            throw;
        }
    }

    private async Task<MailboxSyncResult> SnapshotAsync(MailboxSyncContext context, CancellationToken cancellationToken)
    {
        // Take the checkpoint before listing: arrivals/label changes during the snapshot are replayed next time.
        var historyId = await _gmail.GetHistoryIdAsync(context.UserId, context.MailboxId, cancellationToken);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var pageTokens = new HashSet<string>(StringComparer.Ordinal);
        string? next = null;
        for (var pageNumber = 0; pageNumber < 10; pageNumber++)
        {
            var page = await _gmail.ListInboxAsync(context.UserId, context.MailboxId, InitialMessageLimit - ids.Count, next, cancellationToken);
            foreach (var id in page.MessageIds.Take(InitialMessageLimit - ids.Count)) ids.Add(id);
            next = page.NextPageToken;
            if (ids.Count >= InitialMessageLimit || string.IsNullOrEmpty(next))
                return await SaveAsync(context, ids, historyId, true, cancellationToken);
            if (!pageTokens.Add(next)) break;
        }
        throw new AppException(ErrorCode.MailboxUnavailable, "Gmail returned invalid inbox pagination.");
    }

    private async Task<MailboxSyncResult> IncrementalAsync(MailboxSyncContext context, CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var pageTokens = new HashSet<string>(StringComparer.Ordinal);
        string? next = null;
        for (var pageNumber = 0; pageNumber < 10; pageNumber++)
        {
            var page = await _gmail.ListHistoryAsync(context.UserId, context.MailboxId, context.HistoryId!, next, cancellationToken);
            ids.UnionWith(page.ChangedMessageIds);
            next = page.NextPageToken;
            if (ids.Count > 500) return await SnapshotAsync(context, cancellationToken);
            if (string.IsNullOrEmpty(next)) return await SaveAsync(context, ids, page.HistoryId, false, cancellationToken);
            if (!pageTokens.Add(next))
                throw new AppException(ErrorCode.MailboxUnavailable, "Gmail returned invalid history pagination.");
        }
        // Rebase the recent inbox instead of skipping an unfinished history page or an unbounded backlog.
        return await SnapshotAsync(context, cancellationToken);
    }

    private async Task<MailboxSyncResult> SaveAsync(MailboxSyncContext context, IEnumerable<string> ids,
        string historyId, bool snapshot, CancellationToken cancellationToken)
    {
        var messages = new List<ProviderEmail>();
        var removed = new List<string>();
        foreach (var id in ids)
        {
            var message = await _gmail.GetMessageAsync(context.UserId, context.MailboxId, id, cancellationToken);
            if (message is null || !message.Labels.Contains("INBOX")
                || message.Labels.Contains("TRASH") || message.Labels.Contains("SPAM")) removed.Add(id);
            else messages.Add(message);
        }
        // Messages and checkpoint are committed together only after every provider request succeeds.
        return await _store.CompleteAsync(context, messages, removed, historyId, snapshot, cancellationToken);
    }
}
