using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Project_AI.Application.Interfaces;
using Project_AI.Infrastructure.BackgroundJobs;
using Project_AI.Infrastructure.Data;
using Project_AI.Infrastructure.Data.Identity;
using Project_AI.Infrastructure.Health;
using Project_AI.Infrastructure.Options;
using Project_AI.Infrastructure.Repositories;
using Project_AI.Infrastructure.Services;

namespace Project_AI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services,
        IConfiguration config, IHostEnvironment environment)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddOptions<JwtOptions>().Bind(config.GetSection(JwtOptions.Section)).ValidateDataAnnotations()
            .Validate(x => x.HasValidSigningKey(), "Jwt:SigningKey must be base64 containing at least 32 random bytes.")
            .ValidateOnStart();
        services.AddOptions<EmailOptions>().Bind(config.GetSection(EmailOptions.Section)).ValidateDataAnnotations()
            .Validate(x => !x.DeliveryEnabled || (!string.IsNullOrWhiteSpace(x.ApiKey)
                && new EmailAddressAttribute().IsValid(x.FromEmail) && !string.IsNullOrWhiteSpace(x.FromEmail)),
                "Configure Email:ApiKey and a verified Email:FromEmail when email delivery is enabled.")
            .Validate(x => environment.IsDevelopment() || x.DeliveryEnabled,
                "Email delivery must be enabled outside Development.")
            .Validate(x => environment.IsDevelopment() || (Uri.TryCreate(x.FrontendBaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https"),
                "The frontend URL must use HTTPS outside Development.").ValidateOnStart();

        services.AddDbContext<AuthDbContext>(x => x.UseNpgsql(
            config.GetConnectionString("Postgres") ?? throw new InvalidOperationException("Configure ConnectionStrings:Postgres.")));
        services.AddIdentityCore<ApplicationUser>(x =>
        {
            x.User.RequireUniqueEmail = true;
            x.Password.RequiredLength = 12;
            x.Password.RequiredUniqueChars = 4;
            x.Password.RequireDigit = true;
            x.Password.RequireLowercase = true;
            x.Password.RequireUppercase = true;
            x.Password.RequireNonAlphanumeric = true;
            x.Lockout.MaxFailedAccessAttempts = 5;
            x.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            x.Lockout.AllowedForNewUsers = true;
            x.SignIn.RequireConfirmedEmail = true;
        }).AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<AuthDbContext>().AddDefaultTokenProviders();
        // Both email-confirmation and reset links expire in one hour.
        services.Configure<DataProtectionTokenProviderOptions>(x => x.TokenLifespan = TimeSpan.FromHours(1));
        services.Configure<PasswordHasherOptions>(x => x.IterationCount = 210_000);

        var protection = services.AddDataProtection().SetApplicationName("InboxAgent");
        var keyPath = config["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keyPath)) protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));

        services.AddSingleton<RedisConnection>();
        services.AddSingleton<TokenBlacklist>();
        services.AddSingleton<JwtTokenIssuer>();
        services.AddScoped<IIdentityAccountService, IdentityAccountService>();
        services.AddScoped<IAuthSessionService, AuthSessionService>();
        services.AddScoped<AuthSessionRepository>();
        services.AddScoped<IAuthEmailSender, AuthEmailSender>();
        services.AddHttpClient<SendGridTransport>(x =>
        {
            x.BaseAddress = new Uri("https://api.sendgrid.com/");
            x.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddHostedService<EmailOutboxWorker>();
        services.AddHealthChecks().AddCheck<DependenciesHealthCheck>("auth_dependencies");
        return services;
    }
}
