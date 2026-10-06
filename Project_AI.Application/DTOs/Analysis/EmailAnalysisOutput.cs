using Project_AI.Domain.Enums;

namespace Project_AI.Application.DTOs.Analysis;

public sealed record EmailAnalysisOutput(string Summary, EmailCategory Category, EmailPriority Priority,
    double Confidence, bool InputTruncated, string Provider, string Model);
