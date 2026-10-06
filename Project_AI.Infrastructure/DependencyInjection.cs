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
        IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSingleton(TimeProvider.System);
        AddConfigurationOptions(services, configuration, environment);
        AddData(services, configuration);
        AddIdentity(services);
        AddAuthServices(services);
        AddEmail(services);
        AddMailboxes(services, configuration, environment);
        services.AddHealthChecks().AddCheck<DependenciesHealthCheck>("auth_dependencies");
        return services;
    }

    private static void AddConfigurationOptions(IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.Section))
            .ValidateDataAnnotations()
            .Validate(options => options.HasValidSigningKey(),
                "Jwt:SigningKey must be base64 containing at least 32 random bytes.")
            .ValidateOnStart();
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.Section))
            .ValidateDataAnnotations()
            .Validate(options => !options.DeliveryEnabled || (!string.IsNullOrWhiteSpace(options.ApiKey)
                && !string.IsNullOrWhiteSpace(options.FromEmail)
                && new EmailAddressAttribute().IsValid(options.FromEmail)),
                "Configure Email:ApiKey and a verified Email:FromEmail when email delivery is enabled.")
            .Validate(options => environment.IsDevelopment() || options.DeliveryEnabled,
                "Email delivery must be enabled outside Development.")
            .Validate(options => environment.IsDevelopment() ||
                (Uri.TryCreate(options.FrontendBaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https"),
                "The frontend URL must use HTTPS outside Development.")
            .ValidateOnStart();
    }

    private static void AddData(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AuthDbContext>(options => options.UseNpgsql(
            configuration.GetConnectionString("Postgres")
                ?? throw new InvalidOperationException("Configure ConnectionStrings:Postgres.")));

        var protection = services.AddDataProtection().SetApplicationName("InboxAgent");
        var keyPath = configuration["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keyPath))
        {
            protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
        }
    }

    private static void AddIdentity(IServiceCollection services)
    {
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 12;
            options.Password.RequiredUniqueChars = 4;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.AllowedForNewUsers = true;
            options.SignIn.RequireConfirmedEmail = true;
        })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddDefaultTokenProviders();

        services.Configure<DataProtectionTokenProviderOptions>(options =>
            options.TokenLifespan = TimeSpan.FromHours(1));
        services.Configure<PasswordHasherOptions>(options => options.IterationCount = 210_000);
    }

    private static void AddAuthServices(IServiceCollection services)
    {
        services.AddSingleton<RedisConnection>();
        services.AddSingleton<TokenBlacklist>();
        services.AddSingleton<JwtTokenIssuer>();
        services.AddScoped<IIdentityAccountService, IdentityAccountService>();
        services.AddScoped<IAuthSessionService, AuthSessionService>();
        services.AddScoped<AuthSessionRepository>();
    }

    private static void AddEmail(IServiceCollection services)
    {
        services.AddScoped<IAuthEmailSender, AuthEmailSender>();
        services.AddHttpClient<SendGridTransport>(client =>
        {
            client.BaseAddress = new Uri("https://api.sendgrid.com/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddHostedService<EmailOutboxWorker>();
    }

    private static void AddMailboxes(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<GmailOptions>().Bind(configuration.GetSection(GmailOptions.Section))
            .Validate(options => !options.Enabled || (!string.IsNullOrWhiteSpace(options.ClientId)
                && !string.IsNullOrWhiteSpace(options.ClientSecret)), "Configure Gmail:ClientId and Gmail:ClientSecret.")
            .Validate(options => !options.Enabled || options.HasValidRedirectUri(environment.IsDevelopment()),
                "Gmail:RedirectUri must use HTTPS, or a loopback HTTP URI in Development, with the Gmail callback path.")
            .ValidateOnStart();
        services.AddHttpClient<IGoogleOAuthClient, GoogleOAuthClient>(client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddSingleton<MailboxTokenProtector>();
        services.AddScoped<IMailboxConnectionStore, MailboxConnectionRepository>();
        services.AddScoped<MailboxAccessTokenService>();
        services.AddScoped<IEmailSyncStore, EmailSyncRepository>();
        services.AddScoped<IEmailQueryStore, EmailQueryRepository>();
        services.AddHttpClient<IGmailMessageClient, GmailMessageClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.MaxResponseContentBufferSize = 8 * 1024 * 1024;
        });
        services.AddScoped<IMailboxOAuthRequestStore, MailboxOAuthRequestStore>();
    }
}
