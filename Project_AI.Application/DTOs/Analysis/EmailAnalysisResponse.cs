namespace Project_AI.Application.DTOs.Analysis;

public sealed record EmailAnalysisResponse(Guid EmailId, string Summary, string Category, string Priority,
    double Confidence, bool InputTruncated, string Provider, string Model, DateTimeOffset AnalyzedAt);
public sealed record EmailAnalysisStatus(string Status, EmailAnalysisResponse? Result,
    DateTimeOffset? LastAttemptAt, string? LastErrorCode);
