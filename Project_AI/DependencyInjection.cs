using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Project_AI.API.ExceptionHandling;
using Project_AI.API.Options;
using Project_AI.API.Responses;
using Project_AI.API.Security;
using Project_AI.Application.Common.Enums;
using Project_AI.Infrastructure.Options;

namespace Project_AI.API;

public static class DependencyInjection
{
    public static IServiceCollection AddPresentation(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        AddHttpEndpoints(services);
        AddBrowserSecurity(services, configuration, environment);
        AddJwtAuthentication(services, configuration);
        AddAuthRateLimiting(services, configuration);
        return services;
    }

    private static void AddHttpEndpoints(IServiceCollection services)
    {
        services.AddControllers().ConfigureApiBehaviorOptions(options =>
            options.InvalidModelStateResponseFactory = CreateValidationResponse);
        services.AddOpenApi();
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            ErrorResponse.Enrich(context.HttpContext, context.ProblemDetails));
        services.AddExceptionHandler<ApiExceptionHandler>();
    }

    private static IActionResult CreateValidationResponse(ActionContext context)
    {
        var errors = context.ModelState.Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(entry => entry.Key, entry => entry.Value!.Errors
                .Select(error => string.IsNullOrEmpty(error.ErrorMessage)
                    ? "The input value is invalid." : error.ErrorMessage).ToArray());
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        return new BadRequestObjectResult(ErrorResponse.Create(context.HttpContext,
            ErrorCode.ValidationFailed, errors: errors))
        {
            ContentTypes = { "application/problem+json" }
        };
    }

    private static void AddBrowserSecurity(IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<AuthWebOptions>().Bind(configuration.GetSection(AuthWebOptions.Section))
            .Validate(options => options.AllowedOrigins.Length > 0 && options.AllowedOrigins.All(origin =>
                Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                && uri.GetLeftPart(UriPartial.Authority) == origin && string.IsNullOrEmpty(uri.UserInfo)),
                "AuthWeb:AllowedOrigins must contain exact origins without trailing slashes or wildcards.")
            .Validate(options => environment.IsDevelopment() || (!options.AllowInsecureCookiesForDevelopment
                && options.AllowedOrigins.All(origin => origin.StartsWith("https://", StringComparison.Ordinal))),
                "Production requires secure cookies and HTTPS frontend origins.")
            .Validate(options => options.SameSite is "Lax" or "Strict" or "None",
                "AuthWeb:SameSite must be Lax, Strict or None.")
            .Validate(options => options.SameSite != "None" || !options.AllowInsecureCookiesForDevelopment,
                "SameSite=None requires secure cookies.")
            .ValidateOnStart();

        var settings = configuration.GetSection(AuthWebOptions.Section).Get<AuthWebOptions>() ?? new();
        services.AddCors(options => options.AddPolicy("frontend", policy => policy
            .WithOrigins(settings.AllowedOrigins)
            .WithMethods("GET", "POST", "PUT", "DELETE")
            .WithHeaders("Content-Type", "Authorization", "X-InboxAgent-CSRF")
            .AllowCredentials()));
    }

    private static void AddJwtAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new();
        if (!settings.HasValidSigningKey())
        {
            throw new InvalidOperationException(
                "Configure Jwt:SigningKey (base64, at least 32 random bytes). See README.md.");
        }

        services.AddScoped<AuthJwtEvents>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.IncludeErrorDetails = false;
            options.EventsType = typeof(AuthJwtEvents);
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = settings.Issuer,
                ValidateAudience = true,
                ValidAudience = settings.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(settings.SigningKey)),
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ClockSkew = TimeSpan.Zero,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                NameClaimType = "sub",
                RoleClaimType = "role"
            };
        });
        services.AddAuthorization();
    }

    private static void AddAuthRateLimiting(IServiceCollection services, IConfiguration configuration)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = (int)HttpStatusCode.TooManyRequests;
            options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Clamp(configuration.GetValue<int?>("RateLimiting:AuthPermitLimit") ?? 20, 1, 1000),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await ErrorResponse.WriteAsync(context.HttpContext, ErrorCode.TooManyRequests);
            };
        });
    }
}
