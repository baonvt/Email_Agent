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
    public DbSet<MailboxConnection> MailboxConnections => Set<MailboxConnection>();
    public DbSet<MailboxCredential> MailboxCredentials => Set<MailboxCredential>();
    public DbSet<EmailMessage> EmailMessages => Set<EmailMessage>();
    public DbSet<MailboxSyncState> MailboxSyncStates => Set<MailboxSyncState>();
    public DbSet<EmailAnalysis> EmailAnalyses => Set<EmailAnalysis>();
    public DbSet<EmailAnalysisRequest> EmailAnalysisRequests => Set<EmailAnalysisRequest>();

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
        builder.Entity<MailboxConnection>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Provider).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            b.Property(x => x.ProviderAccountId).HasMaxLength(255);
            b.Property(x => x.Email).HasMaxLength(320);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasIndex(x => new { x.UserId, x.Provider }).IsUnique();
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<MailboxCredential>(b =>
        {
            b.HasKey(x => x.MailboxConnectionId);
            b.HasOne<MailboxConnection>().WithOne().HasForeignKey<MailboxCredential>(x => x.MailboxConnectionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<EmailMessage>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.ProviderMessageId).HasMaxLength(128);
            b.Property(x => x.ThreadId).HasMaxLength(128);
            b.Property(x => x.Subject).HasMaxLength(2000);
            b.Property(x => x.From).HasMaxLength(4000);
            b.Property(x => x.To).HasMaxLength(8000);
            b.Property(x => x.Snippet).HasMaxLength(2000);
            b.HasIndex(x => new { x.MailboxConnectionId, x.ProviderMessageId }).IsUnique();
            b.HasIndex(x => new { x.MailboxConnectionId, x.ReceivedAt, x.Id });
            b.HasOne<MailboxConnection>().WithMany().HasForeignKey(x => x.MailboxConnectionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<MailboxSyncState>(b =>
        {
            b.HasKey(x => x.MailboxConnectionId);
            b.Property(x => x.HistoryId).HasMaxLength(32);
            b.Property(x => x.LastErrorCode).HasMaxLength(64);
            b.HasOne<MailboxConnection>().WithOne().HasForeignKey<MailboxSyncState>(x => x.MailboxConnectionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<EmailAnalysis>(b =>
        {
            b.HasKey(x => x.EmailMessageId);
            b.Property(x => x.Summary).HasMaxLength(2000);
            b.Property(x => x.Category).HasConversion<string>().HasMaxLength(32);
            b.Property(x => x.Priority).HasConversion<string>().HasMaxLength(16);
            b.Property(x => x.Provider).HasMaxLength(32);
            b.Property(x => x.Model).HasMaxLength(100);
            b.HasOne<EmailMessage>().WithOne().HasForeignKey<EmailAnalysis>(x => x.EmailMessageId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<EmailAnalysisRequest>(b =>
        {
            b.HasKey(x => x.EmailMessageId);
            b.Property(x => x.LastErrorCode).HasMaxLength(64);
            b.HasOne<EmailMessage>().WithOne().HasForeignKey<EmailAnalysisRequest>(x => x.EmailMessageId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
