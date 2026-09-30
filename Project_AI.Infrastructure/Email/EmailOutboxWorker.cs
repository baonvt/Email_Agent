using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;


using Project_AI.Infrastructure.Persistence;

namespace Project_AI.Infrastructure.Email;

public sealed class EmailOutboxWorker(IServiceScopeFactory scopes, IOptions<EmailOptions> options,
    TimeProvider clock, ILogger<EmailOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.DeliveryEnabled)
        {
            logger.LogWarning("Email delivery is disabled. Auth emails remain encrypted in the outbox; configure SendGrid and restart to deliver them.");
            return;
        }
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessNextAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError("Email outbox processing failed ({ErrorType}).", ex.GetType().Name); }
            if (!await timer.WaitForNextTickAsync(stoppingToken)) return;
        }
    }

    private async Task ProcessNextAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var now = clock.GetUtcNow();
        // Discard old mail instead of delivering expired links; retain sent/failed mail for at most a day.
        await db.EmailOutbox.Where(x => x.CreatedAt < now.AddDays(-1)).ExecuteDeleteAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var message = (await db.EmailOutbox.FromSqlInterpolated(
            $"SELECT * FROM \"EmailOutbox\" WHERE \"SentAt\" IS NULL AND \"Attempts\" < 8 AND \"NextAttemptAt\" <= {now} AND \"CreatedAt\" > {now.AddHours(-1)} ORDER BY \"CreatedAt\" LIMIT 1 FOR UPDATE SKIP LOCKED")
            .ToListAsync(ct)).SingleOrDefault();
        if (message is null) return;
        try
        {
            var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("InboxAgent.AuthEmail.v1");
            var payload = JsonSerializer.Deserialize<EmailPayload>(protector.Unprotect(message.ProtectedPayload))!;
            await scope.ServiceProvider.GetRequiredService<SendGridTransport>().SendAsync(payload, ct);
            message.SentAt = now;
            message.ProtectedPayload = "";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            message.NextAttemptAt = now.AddSeconds(Math.Min(1800, Math.Pow(2, message.Attempts) * 10));
            logger.LogWarning("Auth email {MessageId} delivery attempt failed ({ErrorType}).", message.Id, ex.GetType().Name);
        }
        message.Attempts++;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
