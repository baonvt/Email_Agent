using Microsoft.Extensions.Diagnostics.HealthChecks;
using Project_AI.Infrastructure.Data;
using Project_AI.Infrastructure.Services;

namespace Project_AI.Infrastructure.Health;

public sealed class DependenciesHealthCheck : IHealthCheck
{
    private readonly AuthDbContext _dbContext;
    private readonly RedisConnection _redisConnection;

    public DependenciesHealthCheck(AuthDbContext dbContext, RedisConnection redisConnection)
    {
        _dbContext = dbContext;
        _redisConnection = redisConnection;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await _dbContext.Database.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy();
            }
            await (await _redisConnection.GetAsync()).GetDatabase().PingAsync().WaitAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return HealthCheckResult.Unhealthy();
        }
    }
}
