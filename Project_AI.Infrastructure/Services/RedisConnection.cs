using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace Project_AI.Infrastructure.Services;

// Connect asynchronously so a cold connection never blocks the ASP.NET request thread pool.
public sealed class RedisConnection(IConfiguration config) : IAsyncDisposable
{
    private readonly Lazy<Task<ConnectionMultiplexer>> connection = new(() =>
    {
        var options = ConfigurationOptions.Parse(config.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:Redis."));
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 2000;
        options.AsyncTimeout = 2000;
        options.SyncTimeout = 2000;
        return ConnectionMultiplexer.ConnectAsync(options);
    });

    public Task<ConnectionMultiplexer> GetAsync() => connection.Value;

    public async ValueTask DisposeAsync()
    {
        if (connection.IsValueCreated)
            await (await connection.Value).DisposeAsync();
    }
}
