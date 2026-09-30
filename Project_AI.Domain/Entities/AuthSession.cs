namespace Project_AI.Domain.Entities;

public sealed class AuthSession
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string SecurityStamp { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    private AuthSession() { }

    public AuthSession(Guid userId, string securityStamp, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        SecurityStamp = securityStamp;
        CreatedAt = now;
        ExpiresAt = expiresAt;
    }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
