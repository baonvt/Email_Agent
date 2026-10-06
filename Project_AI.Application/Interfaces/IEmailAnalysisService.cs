using Project_AI.Application.DTOs.Analysis;

namespace Project_AI.Application.Interfaces;

public interface IEmailAnalysisService
{
    Task<EmailAnalysisResponse> AnalyzeAsync(Guid userId, Guid mailboxId, Guid emailId, bool force, CancellationToken cancellationToken);
    Task<EmailAnalysisStatus> GetAsync(Guid userId, Guid mailboxId, Guid emailId, CancellationToken cancellationToken);
}
