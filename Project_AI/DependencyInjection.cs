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
        IConfiguration config, IHostEnvironment environment)
    {
        services.AddControllers().ConfigureApiBehaviorOptions(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
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
            };
        });
        services.AddOpenApi();
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            ErrorResponse.Enrich(context.HttpContext, context.ProblemDetails));
        services.AddExceptionHandler<ApiExceptionHandler>();

        services.AddOptions<AuthWebOptions>().Bind(config.GetSection(AuthWebOptions.Section))
            .Validate(x => x.AllowedOrigins.Length > 0 && x.AllowedOrigins.All(origin =>
                Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                && uri.GetLeftPart(UriPartial.Authority) == origin && string.IsNullOrEmpty(uri.UserInfo)),
                "AuthWeb:AllowedOrigins must contain exact origins without trailing slashes or wildcards.")
            .Validate(x => environment.IsDevelopment() || (!x.AllowInsecureCookiesForDevelopment
                && x.AllowedOrigins.All(origin => origin.StartsWith("https://", StringComparison.Ordinal))),
                "Production requires secure cookies and HTTPS frontend origins.")
            .Validate(x => x.SameSite is "Lax" or "Strict" or "None", "AuthWeb:SameSite must be Lax, Strict or None.")
            .Validate(x => x.SameSite != "None" || !x.AllowInsecureCookiesForDevelopment,
                "SameSite=None requires secure cookies.").ValidateOnStart();
        var web = config.GetSection(AuthWebOptions.Section).Get<AuthWebOptions>() ?? new();
        services.AddCors(x => x.AddPolicy("frontend", policy => policy.WithOrigins(web.AllowedOrigins)
            .WithMethods("GET", "POST").WithHeaders("Content-Type", "Authorization", "X-InboxAgent-CSRF").AllowCredentials()));
        var jwt = config.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new();
        if (!jwt.HasValidSigningKey()) throw new InvalidOperationException("Configure Jwt:SigningKey (base64, at least 32 random bytes). See README.md.");
        services.AddScoped<AuthJwtEvents>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(x =>
        {
            x.MapInboundClaims = false;
            x.IncludeErrorDetails = false;
            x.EventsType = typeof(AuthJwtEvents);
            x.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = jwt.Issuer,
                ValidateAudience = true, ValidAudience = jwt.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(jwt.SigningKey)),
                ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
                ClockSkew = TimeSpan.Zero, ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                NameClaimType = "sub", RoleClaimType = "role"
            };
        });
        services.AddAuthorization();
        services.AddRateLimiter(x =>
        {
            x.RejectionStatusCode = (int)HttpStatusCode.TooManyRequests;
            x.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Clamp(config.GetValue<int?>("RateLimiting:AuthPermitLimit") ?? 20, 1, 1000),
                    Window = TimeSpan.FromMinutes(1), QueueLimit = 0
                }));
            x.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await ErrorResponse.WriteAsync(context.HttpContext, ErrorCode.TooManyRequests);
            };
        });
        return services;
    }
}
