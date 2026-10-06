using Project_AI.Application.DTOs.Analysis;

namespace Project_AI.Application.Interfaces;

public interface IEmailAnalysisStore
{
    Task<EmailAnalysisContext> BeginAsync(Guid userId, Guid mailboxId, Guid emailId, bool force, CancellationToken cancellationToken);
    Task<EmailAnalysisResponse> CompleteAsync(EmailAnalysisContext context, EmailAnalysisOutput output, CancellationToken cancellationToken);
    Task FailAsync(EmailAnalysisContext context, string errorCode, CancellationToken cancellationToken);
    Task<EmailAnalysisStatus> GetAsync(Guid userId, Guid mailboxId, Guid emailId, CancellationToken cancellationToken);
}
