using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace Project_AI.Infrastructure.Services;

public sealed class RedisConnection : IAsyncDisposable
{
    private readonly Lazy<Task<ConnectionMultiplexer>> _connection;

    public RedisConnection(IConfiguration configuration)
    {
        // Share one asynchronous connection; the multiplexer reconnects after an outage.
        _connection = new Lazy<Task<ConnectionMultiplexer>>(() =>
        {
            var options = ConfigurationOptions.Parse(configuration.GetConnectionString("Redis")
                ?? throw new InvalidOperationException("Configure ConnectionStrings:Redis."));
            options.AbortOnConnectFail = false;
            options.ConnectTimeout = 2000;
            options.AsyncTimeout = 2000;
            options.SyncTimeout = 2000;
            return ConnectionMultiplexer.ConnectAsync(options);
        });
    }

    public Task<ConnectionMultiplexer> GetAsync() => _connection.Value;

    public async ValueTask DisposeAsync()
    {
        if (_connection.IsValueCreated)
        {
            await (await _connection.Value).DisposeAsync();
        }
    }
}
