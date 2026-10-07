using System.Text.Json;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Auth;
using Project_AI.Application.DTOs.Replies;
using Project_AI.Application.Interfaces;
using Project_AI.Domain.Enums;

namespace Project_AI.Application.Services;

public sealed class ReplySendService : IReplySendService
{
    private readonly IReplySendStore _store;
    private readonly IReplyDraftStore _drafts;
    private readonly IGmailReplyClient _gmail;
    public ReplySendService(IReplySendStore store, IReplyDraftStore drafts, IGmailReplyClient gmail)
    {
        _store = store; _drafts = drafts; _gmail = gmail;
    }

    public async Task<ReplyDraftResponse> SendAsync(AccessTokenContext session, Guid mailboxId, Guid draftId,
        Guid expectedVersion, CancellationToken cancellationToken)
    {
        var preview = await _store.ReadAsync(session.UserId, mailboxId, draftId, expectedVersion, cancellationToken);
        if (preview.Draft.Status == ReplyDraftStatus.Sent)
            return await _drafts.GetAsync(session.UserId, mailboxId, draftId, cancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        var thread = await _gmail.GetThreadAsync(preview.Source!, deadline.Token);
        ReplyContentGuard.EnsureCurrentSource(preview.Source!, thread);
        if (thread.Fingerprint != preview.Draft.ThreadFingerprint || thread.To != preview.Draft.To
            || thread.InReplyTo != preview.Draft.InReplyTo)
            throw new AppException(ErrorCode.DraftOutdated, "The conversation or reply target changed. Regenerate and review.");
        var context = await _store.BeginAsync(session, mailboxId, draftId, expectedVersion, deadline.Token);
        if (context.Draft.Status == ReplyDraftStatus.Sent)
            return await _drafts.GetAsync(session.UserId, mailboxId, draftId, cancellationToken);
        try
        {
            var result = await _gmail.SendAsync(context, deadline.Token);
            // Persist acceptance even if the browser disconnected after Gmail accepted the request.
            using var completion = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            return await _store.CompleteAsync(context, result, completion.Token);
        }
        catch (Exception exception)
        {
            var uncertain = exception is not ReplySendException send || send.OutcomeUnknown;
            var code = exception is ReplySendException known ? known.Code : ErrorCode.ReplySendUnknown;
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _store.FailAsync(context, uncertain, JsonNamingPolicy.SnakeCaseLower.ConvertName(code.ToString()), cleanup.Token);
            }
            catch (Exception) { /* Never unlock a send after an unknown external outcome. */ }
            throw new AppException(uncertain ? ErrorCode.ReplySendUnknown : code, "The reply could not be completed.");
        }
    }
}
