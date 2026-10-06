using Project_AI.Application.DTOs.Analysis;

namespace Project_AI.Application.Interfaces;

public interface IEmailAnalyzer
{
    Task<EmailAnalysisOutput> AnalyzeAsync(EmailAnalysisInput email, CancellationToken cancellationToken);
}
