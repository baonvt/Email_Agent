using Project_AI.Domain.Enums;

namespace Project_AI.Domain.Entities;

public sealed class ReplyDraft
{
    public Guid Id { get; private set; }
    public Guid EmailMessageId { get; private set; }
    public Guid Version { get; private set; }
    public Guid SourceVersion { get; private set; }
    public Guid MailboxVersion { get; private set; }
    public ReplyDraftStatus Status { get; private set; }
    public string From { get; private set; } = "";
    public string To { get; private set; } = "";
    public string Subject { get; private set; } = "";
    public string BodyText { get; private set; } = "";
    public string InReplyTo { get; private set; } = "";
    public string[] References { get; private set; } = [];
    public string ThreadId { get; private set; } = "";
    public string ThreadFingerprint { get; private set; } = "";
    public bool InputTruncated { get; private set; }
    public string Model { get; private set; } = "";
    public Guid? GenerationId { get; private set; }
    public DateTimeOffset? GenerationExpiresAt { get; private set; }
    public Guid? ApprovedVersion { get; private set; }
    public Guid? SendAttemptId { get; private set; }
    public string OutgoingMessageId { get; private set; } = "";
    public string? ProviderMessageId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? SendStartedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public string? LastErrorCode { get; private set; }

    private ReplyDraft() { }
    public ReplyDraft(Guid emailMessageId, DateTimeOffset now)
    {
        Id = Guid.NewGuid(); EmailMessageId = emailMessageId;
        Version = Guid.NewGuid(); CreatedAt = now; UpdatedAt = now;
        Status = ReplyDraftStatus.GenerationFailed;
    }

    public void StartGeneration(DateTimeOffset now)
    {
        if (Status is ReplyDraftStatus.Sending or ReplyDraftStatus.Sent or ReplyDraftStatus.SendUnknown
            || (Status == ReplyDraftStatus.Generating && GenerationExpiresAt > now))
            throw new InvalidOperationException("The draft cannot be generated in its current state.");
        Status = ReplyDraftStatus.Generating;
        GenerationId = Guid.NewGuid(); GenerationExpiresAt = now.AddMinutes(2);
        LastErrorCode = null; Touch(now);
    }

    public void CompleteGeneration(Guid sourceVersion, Guid mailboxVersion, string from, string to, string subject,
        string body, string inReplyTo, string[] references, string threadId, string fingerprint,
        bool inputTruncated, string model, DateTimeOffset now)
    {
        if (Status != ReplyDraftStatus.Generating) throw new InvalidOperationException("Generation is not running.");
        ValidateBody(body);
        SourceVersion = sourceVersion; MailboxVersion = mailboxVersion;
        From = from; To = to; Subject = subject; BodyText = body;
        InReplyTo = inReplyTo; References = references.ToArray(); ThreadId = threadId; ThreadFingerprint = fingerprint;
        InputTruncated = inputTruncated; Model = model; Status = ReplyDraftStatus.Draft;
        GenerationId = null; GenerationExpiresAt = null; LastErrorCode = null; Touch(now);
    }

    public void FailGeneration(string errorCode, DateTimeOffset now)
    {
        if (Status != ReplyDraftStatus.Generating) throw new InvalidOperationException("Generation is not running.");
        Status = BodyText.Length == 0 ? ReplyDraftStatus.GenerationFailed : ReplyDraftStatus.Draft;
        GenerationId = null; GenerationExpiresAt = null; LastErrorCode = errorCode; Touch(now);
    }

    public void Edit(string body, DateTimeOffset now)
    {
        if (Status != ReplyDraftStatus.Draft) throw new InvalidOperationException("Only ready drafts can be edited.");
        ValidateBody(body); BodyText = body; LastErrorCode = null; Touch(now);
    }

    public void StartSend(Guid approvedVersion, DateTimeOffset now)
    {
        if (Status != ReplyDraftStatus.Draft || Version != approvedVersion)
            throw new InvalidOperationException("The approved draft changed.");
        ApprovedVersion = approvedVersion; SendAttemptId = Guid.NewGuid();
        OutgoingMessageId = $"{SendAttemptId.Value:N}@inboxagent.local";
        Status = ReplyDraftStatus.Sending; SendStartedAt = now; LastErrorCode = null; Touch(now);
    }

    public void CompleteSend(string providerMessageId, DateTimeOffset now)
    {
        if (Status != ReplyDraftStatus.Sending) throw new InvalidOperationException("Sending is not running.");
        ArgumentException.ThrowIfNullOrWhiteSpace(providerMessageId);
        ProviderMessageId = providerMessageId; SentAt = now; Status = ReplyDraftStatus.Sent; Touch(now);
    }

    public void FailSend(bool outcomeUnknown, string errorCode, DateTimeOffset now)
    {
        if (Status != ReplyDraftStatus.Sending) throw new InvalidOperationException("Sending is not running.");
        Status = outcomeUnknown ? ReplyDraftStatus.SendUnknown : ReplyDraftStatus.Draft;
        LastErrorCode = errorCode; Touch(now);
    }

    private void Touch(DateTimeOffset now) { Version = Guid.NewGuid(); UpdatedAt = now; }
    private static void ValidateBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > 10000 || body.Contains('\0'))
            throw new ArgumentException("The reply body must contain 1 to 10000 characters without NUL.");
    }
}
