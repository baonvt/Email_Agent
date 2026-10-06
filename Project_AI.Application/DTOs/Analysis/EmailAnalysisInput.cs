namespace Project_AI.Application.DTOs.Analysis;

public sealed record EmailAnalysisInput(Guid EmailId, Guid SourceVersion, string Subject,
    string From, string To, string Snippet, string BodyText, bool BodyTruncated, DateTimeOffset ReceivedAt);
public sealed record EmailAnalysisContext(Guid UserId, Guid MailboxId, Guid MailboxVersion,
    EmailAnalysisInput Email, Guid LeaseId, EmailAnalysisResponse? CachedResult);
