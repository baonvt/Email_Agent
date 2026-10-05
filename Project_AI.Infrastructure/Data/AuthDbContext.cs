using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Project_AI.Domain.Entities;
using Project_AI.Infrastructure.Data.Entities;
using Project_AI.Infrastructure.Data.Identity;

namespace Project_AI.Infrastructure.Data;

public sealed class AuthDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options)
    {
    }

    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<EmailOutboxMessage> EmailOutbox => Set<EmailOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>().Property(x => x.DisplayName).HasMaxLength(100);
        builder.Entity<ApplicationUser>().HasIndex(x => x.NormalizedEmail).IsUnique();
        builder.Entity<AuthSession>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.SecurityStamp).HasMaxLength(256);
            b.HasIndex(x => new { x.UserId, x.ExpiresAt });
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<RefreshToken>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.TokenHash).HasMaxLength(64);
            b.HasIndex(x => x.TokenHash).IsUnique();
            b.HasOne<AuthSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<EmailOutboxMessage>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.SentAt, x.NextAttemptAt });
        });
    }
}
