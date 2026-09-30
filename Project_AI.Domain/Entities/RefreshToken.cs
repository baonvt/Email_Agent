namespace Project_AI.Domain.Entities;

public sealed class RefreshToken
{
    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }
    public string TokenHash { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }

    private RefreshToken() { }

    public RefreshToken(Guid sessionId, string tokenHash, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        Id = Guid.NewGuid();
        SessionId = sessionId;
        TokenHash = tokenHash;
        CreatedAt = now;
        ExpiresAt = expiresAt;
    }

    public bool CanUse(DateTimeOffset now) => ConsumedAt is null && ExpiresAt > now;

    public void Consume(DateTimeOffset now)
    {
        if (!CanUse(now)) throw new InvalidOperationException("Refresh token cannot be used.");
        ConsumedAt = now;
    }
}
