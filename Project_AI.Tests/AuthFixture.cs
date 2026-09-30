using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace Project_AI.Tests;

public sealed class AuthFactory(string postgres, string redis, int rateLimit = 1000, string? signingKey = null) : WebApplicationFactory<Program>
{
    public string SigningKey { get; } = signingKey ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Postgres", postgres);
        builder.UseSetting("ConnectionStrings:Redis", redis);
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("Email:DeliveryEnabled", "false");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("AuthWeb:AllowedOrigins:0", "http://localhost:3000");
        builder.UseSetting("AuthWeb:AllowInsecureCookiesForDevelopment", "true");
        builder.UseSetting("RateLimiting:AuthPermitLimit", rateLimit.ToString());
        builder.ConfigureLogging(x => x.ClearProviders());
    }
}

public sealed class AuthFixture : IAsyncLifetime
{
    public AuthFactory Factory { get; private set; } = null!;
    public string Postgres { get; private set; } = "";
    public string Redis { get; private set; } = "";
    private string adminConnection = "";
    private readonly string databaseName = "inboxagent_test_" + Guid.NewGuid().ToString("N");

    public async Task InitializeAsync()
    {
        var config = new ConfigurationBuilder().AddUserSecrets<Program>().AddEnvironmentVariables().Build();
        var pg = new NpgsqlConnectionStringBuilder(config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Run scripts/Initialize-LocalEnvironment.ps1 and docker compose up -d postgres redis first."));
        pg.Database = "postgres";
        adminConnection = pg.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
        await command.ExecuteNonQueryAsync();
        pg.Database = databaseName;
        Postgres = pg.ConnectionString;
        Redis = config.GetConnectionString("Redis")!;
        Factory = new AuthFactory(Postgres, Redis);
        using var client = Factory.CreateClient();
        var actual = Factory.Services.GetService(typeof(IConfiguration)) as IConfiguration;
        Assert.Equal(databaseName, new NpgsqlConnectionStringBuilder(actual!.GetConnectionString("Postgres")).Database);
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        if (string.IsNullOrEmpty(adminConnection)) return;
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        // The only deleted database is this fixture's freshly generated inboxagent_test_<guid>.
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
