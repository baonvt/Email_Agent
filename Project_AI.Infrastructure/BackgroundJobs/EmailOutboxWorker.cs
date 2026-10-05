using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Project_AI.Infrastructure.Data;
using Project_AI.Infrastructure.Models;
using Project_AI.Infrastructure.Options;
using Project_AI.Infrastructure.Services;

namespace Project_AI.Infrastructure.BackgroundJobs;

public sealed class EmailOutboxWorker : BackgroundService
{
    private const int MaxDeliveryAttempts = 8;
    private const int InitialRetryDelaySeconds = 10;
    private const int MaxRetryDelaySeconds = 1800;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LinkLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan MessageRetention = TimeSpan.FromDays(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<EmailOptions> _emailOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EmailOutboxWorker> _logger;

    public EmailOutboxWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<EmailOptions> emailOptions,
        TimeProvider timeProvider,
        ILogger<EmailOutboxWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _emailOptions = emailOptions;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_emailOptions.Value.DeliveryEnabled)
        {
            _logger.LogWarning("Email delivery is disabled. Auth emails remain encrypted in the outbox; configure SendGrid and restart to deliver them.");
            return;
        }
        using var timer = new PeriodicTimer(PollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError("Email outbox processing failed ({ErrorType}).", exception.GetType().Name);
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                return;
            }
        }
    }

    private async Task ProcessNextAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var now = _timeProvider.GetUtcNow();
        // Discard old mail instead of delivering expired links; retain sent/failed mail for at most a day.
        await dbContext.EmailOutbox.Where(x => x.CreatedAt < now - MessageRetention).ExecuteDeleteAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var message = (await dbContext.EmailOutbox.FromSqlInterpolated(
            $"SELECT * FROM \"EmailOutbox\" WHERE \"SentAt\" IS NULL AND \"Attempts\" < {MaxDeliveryAttempts} AND \"NextAttemptAt\" <= {now} AND \"CreatedAt\" > {now - LinkLifetime} ORDER BY \"CreatedAt\" LIMIT 1 FOR UPDATE SKIP LOCKED")
            .ToListAsync(cancellationToken)).SingleOrDefault();
        if (message is null)
        {
            return;
        }

        try
        {
            var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("InboxAgent.AuthEmail.v1");
            var payload = JsonSerializer.Deserialize<EmailPayload>(protector.Unprotect(message.ProtectedPayload))!;
            await scope.ServiceProvider.GetRequiredService<SendGridTransport>().SendAsync(payload, cancellationToken);
            message.SentAt = now;
            message.ProtectedPayload = "";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var retryDelay = Math.Min(MaxRetryDelaySeconds, Math.Pow(2, message.Attempts) * InitialRetryDelaySeconds);
            message.NextAttemptAt = now.AddSeconds(retryDelay);
            _logger.LogWarning("Auth email {MessageId} delivery attempt failed ({ErrorType}).", message.Id, exception.GetType().Name);
        }
        message.Attempts++;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
