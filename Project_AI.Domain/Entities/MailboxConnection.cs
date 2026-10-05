using Project_AI.Domain.Enums;

namespace Project_AI.Domain.Entities;

public sealed class MailboxConnection
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public MailboxProvider Provider { get; private set; }
    public string? ProviderAccountId { get; private set; }
    public string? Email { get; private set; }
    public MailboxStatus Status { get; private set; }
    public Guid Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ConnectedAt { get; private set; }

    private MailboxConnection() { }

    public MailboxConnection(Guid userId, MailboxProvider provider, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Provider = provider;
        Version = Guid.NewGuid();
        CreatedAt = now;
        UpdatedAt = now;
    }

    public void Connect(string providerAccountId, string email, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerAccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ProviderAccountId = providerAccountId;
        Email = email;
        Status = MailboxStatus.Connected;
        ConnectedAt = now;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    public void Disconnect(DateTimeOffset now)
    {
        Status = MailboxStatus.Disconnected;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    public void RequireReconnect(DateTimeOffset now)
    {
        Status = MailboxStatus.RequiresReconnect;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }
}
