using System.Text.Json;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Analysis;
using Project_AI.Application.Interfaces;

namespace Project_AI.Application.Services;

public sealed class EmailAnalysisService : IEmailAnalysisService
{
    private readonly IEmailAnalysisStore _store;
    private readonly IEmailAnalyzer _analyzer;

    public EmailAnalysisService(IEmailAnalysisStore store, IEmailAnalyzer analyzer)
    {
        _store = store;
        _analyzer = analyzer;
    }

    public async Task<EmailAnalysisResponse> AnalyzeAsync(Guid userId, Guid mailboxId, Guid emailId, bool force, CancellationToken cancellationToken)
    {
        var context = await _store.BeginAsync(userId, mailboxId, emailId, force, cancellationToken);
        if (context.CachedResult is not null) return context.CachedResult;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            var output = await _analyzer.AnalyzeAsync(context.Email, deadline.Token);
            Validate(output);
            return await _store.CompleteAsync(context, output, deadline.Token);
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
                // Preserve the original failure; an unavailable database leaves a lease that expires.
            }
            if (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
                throw new AppException(ErrorCode.RequestTimeout, "Analysis timed out. Try again.");
            throw;
        }
    }

    public Task<EmailAnalysisStatus> GetAsync(Guid userId, Guid mailboxId, Guid emailId, CancellationToken cancellationToken) =>
        _store.GetAsync(userId, mailboxId, emailId, cancellationToken);

    private static void Validate(EmailAnalysisOutput output)
    {
        if (string.IsNullOrWhiteSpace(output.Summary) || output.Summary.Length > 2000 || output.Summary.Contains('\0')
            || !Enum.IsDefined(output.Category) || !Enum.IsDefined(output.Priority)
            || !double.IsFinite(output.Confidence) || output.Confidence is < 0 or > 1
            || string.IsNullOrWhiteSpace(output.Provider) || output.Provider.Length > 32 || output.Provider.Contains('\0')
            || string.IsNullOrWhiteSpace(output.Model) || output.Model.Length > 100 || output.Model.Contains('\0'))
            throw new AppException(ErrorCode.AiInvalidResponse, "The AI provider returned an invalid analysis.");
    }
}
