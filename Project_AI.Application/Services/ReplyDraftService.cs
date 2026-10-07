using System.Text.Json;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Replies;
using Project_AI.Application.Interfaces;

namespace Project_AI.Application.Services;

public sealed class ReplyDraftService : IReplyDraftService
{
    private readonly IReplyDraftStore _store;
    private readonly IGmailReplyClient _gmail;
    private readonly IReplyGenerator _generator;
    public ReplyDraftService(IReplyDraftStore store, IGmailReplyClient gmail, IReplyGenerator generator)
    {
        _store = store; _gmail = gmail; _generator = generator;
    }

    public async Task<ReplyDraftResponse> GenerateAsync(Guid userId, Guid mailboxId, Guid emailId,
        bool force, Guid? expectedVersion, CancellationToken cancellationToken)
    {
        var context = await _store.BeginGenerationAsync(userId, mailboxId, emailId, force, expectedVersion, cancellationToken);
        if (context.CachedDraft is not null) return context.CachedDraft;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(75));
        try
        {
            var thread = await _gmail.GetThreadAsync(context.Source, deadline.Token);
            ReplyContentGuard.EnsureCurrentSource(context.Source, thread);
            var output = await _generator.GenerateAsync(context.Source, thread, deadline.Token);
            if (string.IsNullOrWhiteSpace(output.BodyText) || output.BodyText.Length > 10000 || output.BodyText.Contains('\0')
                || string.IsNullOrWhiteSpace(output.Model) || output.Model.Length > 100 || output.Model.Contains('\0'))
                throw new AppException(ErrorCode.AiInvalidResponse, "The generated draft is invalid.");
            return await _store.CompleteGenerationAsync(context, thread, output, deadline.Token);
        }
        catch (Exception exception)
        {
            var code = exception is AppException app ? app.Code
                : exception is OperationCanceledException ? ErrorCode.RequestTimeout : ErrorCode.InternalError;
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _store.FailGenerationAsync(context, JsonNamingPolicy.SnakeCaseLower.ConvertName(code.ToString()), cleanup.Token);
            }
            catch (Exception) { /* Preserve the failure; the generation lease expires. */ }
            if (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
                throw new AppException(ErrorCode.RequestTimeout, "Reply generation timed out.");
            throw;
        }
    }
}
