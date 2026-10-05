using Microsoft.Extensions.Diagnostics.HealthChecks;
using Project_AI.Infrastructure.Data;
using Project_AI.Infrastructure.Services;

namespace Project_AI.Infrastructure.Health;

public sealed class DependenciesHealthCheck(AuthDbContext db, RedisConnection connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            if (!await db.Database.CanConnectAsync(ct)) return HealthCheckResult.Unhealthy();
            await (await connection.GetAsync()).GetDatabase().PingAsync().WaitAsync(ct);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return HealthCheckResult.Unhealthy(); }
    }
}
