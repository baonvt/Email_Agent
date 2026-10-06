namespace Project_AI.Domain.Entities;

public sealed class EmailMessage
{
    public Guid Id { get; private set; }
    public Guid MailboxConnectionId { get; private set; }
    public string ProviderMessageId { get; private set; } = "";
    public string ThreadId { get; private set; } = "";
    public string Subject { get; private set; } = "";
    public string From { get; private set; } = "";
    public string To { get; private set; } = "";
    public string Snippet { get; private set; } = "";
    public string BodyText { get; private set; } = "";
    public bool BodyTruncated { get; private set; }
    public bool HasAttachments { get; private set; }
    public string[] Labels { get; private set; } = [];
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid ContentVersion { get; private set; }

    private EmailMessage() { }

    public EmailMessage(Guid mailboxConnectionId, string providerMessageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerMessageId);
        Id = Guid.NewGuid();
        MailboxConnectionId = mailboxConnectionId;
        ProviderMessageId = providerMessageId;
        ContentVersion = Guid.NewGuid();
    }

    public void Update(string threadId, string subject, string from, string to, string snippet,
        string bodyText, bool bodyTruncated, bool hasAttachments, string[] labels,
        DateTimeOffset receivedAt, DateTimeOffset now)
    {
        if (Subject != subject || From != from || To != to || Snippet != snippet || BodyText != bodyText
            || BodyTruncated != bodyTruncated || ReceivedAt != receivedAt)
            ContentVersion = Guid.NewGuid();
        ThreadId = threadId;
        Subject = subject;
        From = from;
        To = to;
        Snippet = snippet;
        BodyText = bodyText;
        BodyTruncated = bodyTruncated;
        HasAttachments = hasAttachments;
        Labels = labels.Distinct(StringComparer.Ordinal).ToArray();
        ReceivedAt = receivedAt;
        UpdatedAt = now;
    }
}
