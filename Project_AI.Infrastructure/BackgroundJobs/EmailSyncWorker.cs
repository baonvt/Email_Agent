using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.Interfaces;
using Project_AI.Infrastructure.Options;

namespace Project_AI.Infrastructure.BackgroundJobs;

public sealed class EmailSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<EmailSyncOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EmailSyncWorker> _logger;

    public EmailSyncWorker(IServiceScopeFactory scopes, IOptions<EmailSyncOptions> options,
        TimeProvider timeProvider, ILogger<EmailSyncWorker> logger)
    {
        _scopes = scopes;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled) return;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await RunCycleAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception) { _logger.LogWarning("Unable to select mailboxes for synchronization. Will retry."); }
                await Task.Delay(TimeSpan.FromSeconds(30), _timeProvider, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        using var selectionScope = _scopes.CreateScope();
        var store = selectionScope.ServiceProvider.GetRequiredService<IEmailSyncStore>();
        var targets = await store.ListDueAsync(_timeProvider.GetUtcNow().AddSeconds(-_options.Value.IntervalSeconds), cancellationToken);
        foreach (var target in targets)
        {
            using var scope = _scopes.CreateScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<IEmailSyncService>()
                    .SyncAsync(target.UserId, target.MailboxId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (AppException exception)
            {
                _logger.LogInformation("Mailbox {MailboxId} synchronization stopped with {Code}.", target.MailboxId, exception.Code);
            }
            catch (Exception)
            {
                _logger.LogWarning("Mailbox {MailboxId} synchronization failed. Will retry.", target.MailboxId);
            }
        }
    }
}
