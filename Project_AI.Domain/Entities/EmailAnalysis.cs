using Project_AI.Domain.Enums;

namespace Project_AI.Domain.Entities;

public sealed class EmailAnalysis
{
    public Guid EmailMessageId { get; private set; }
    public Guid SourceVersion { get; private set; }
    public string Summary { get; private set; } = "";
    public EmailCategory Category { get; private set; }
    public EmailPriority Priority { get; private set; }
    public double Confidence { get; private set; }
    public bool InputTruncated { get; private set; }
    public string Provider { get; private set; } = "";
    public string Model { get; private set; } = "";
    public DateTimeOffset AnalyzedAt { get; private set; }

    private EmailAnalysis() { }

    public EmailAnalysis(Guid emailMessageId) { EmailMessageId = emailMessageId; }

    public void Update(Guid sourceVersion, string summary, EmailCategory category, EmailPriority priority,
        double confidence, bool inputTruncated, string provider, string model, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        if (summary.Length > 2000 || summary.Contains('\0') || !Enum.IsDefined(category) || !Enum.IsDefined(priority)
            || !double.IsFinite(confidence) || confidence is < 0 or > 1)
            throw new ArgumentException("The email analysis is invalid.");
        SourceVersion = sourceVersion;
        Summary = summary;
        Category = category;
        Priority = priority;
        Confidence = confidence;
        InputTruncated = inputTruncated;
        Provider = provider;
        Model = model;
        AnalyzedAt = now;
    }
}
