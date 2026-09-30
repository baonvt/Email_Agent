using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Project_AI.API.ExceptionHandling;
using Project_AI.API.Middleware;
using Project_AI.Application;

using Project_AI.Infrastructure;
using Project_AI.Infrastructure.Options;

using Project_AI.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddOptions<AuthWebOptions>().Bind(builder.Configuration.GetSection(AuthWebOptions.Section))
    .Validate(x => x.AllowedOrigins.Length > 0 && x.AllowedOrigins.All(origin =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
        && uri.GetLeftPart(UriPartial.Authority) == origin && string.IsNullOrEmpty(uri.UserInfo)),
        "AuthWeb:AllowedOrigins must contain exact origins without trailing slashes or wildcards.")
    .Validate(x => builder.Environment.IsDevelopment() || (!x.AllowInsecureCookiesForDevelopment
        && x.AllowedOrigins.All(origin => origin.StartsWith("https://", StringComparison.Ordinal))),
        "Production requires secure cookies and HTTPS frontend origins.")
    .Validate(x => x.SameSite is "Lax" or "Strict" or "None", "AuthWeb:SameSite must be Lax, Strict or None.")
    .Validate(x => x.SameSite != "None" || !x.AllowInsecureCookiesForDevelopment,
        "SameSite=None requires secure cookies.").ValidateOnStart();
var web = builder.Configuration.GetSection(AuthWebOptions.Section).Get<AuthWebOptions>() ?? new();
builder.Services.AddCors(x => x.AddPolicy("frontend", policy => policy.WithOrigins(web.AllowedOrigins)
    .WithMethods("GET", "POST").WithHeaders("Content-Type", "Authorization", "X-InboxAgent-CSRF").AllowCredentials()));
var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new();
if (!jwt.HasValidSigningKey()) throw new InvalidOperationException("Configure Jwt:SigningKey (base64, at least 32 random bytes). See README.md.");
builder.Services.AddScoped<AuthJwtEvents>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(x =>
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
builder.Services.AddAuthorization(x => x.FallbackPolicy = new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser().Build());
builder.Services.AddRateLimiter(x =>
{
    x.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    x.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Clamp(builder.Configuration.GetValue<int?>("RateLimiting:AuthPermitLimit") ?? 20, 1, 1000),
            Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        }));
    x.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await Results.Problem(statusCode: 429, title: "Too many requests. Try again later.")
            .ExecuteAsync(context.HttpContext);
    };
});

var app = builder.Build();
var migrateOnly = args.Contains("--migrate", StringComparer.Ordinal);
var promoteIndex = Array.IndexOf(args, "--promote-admin");
await AuthDatabaseInitializer.InitializeAsync(app.Services,
    migrateOnly || app.Configuration.GetValue<bool>("Database:MigrateOnStartup"));
if (promoteIndex >= 0)
{
    if (promoteIndex + 1 >= args.Length) throw new ArgumentException("Provide --promote-admin email.");
    await AuthDatabaseInitializer.PromoteAdminAsync(app.Services, args[promoteIndex + 1]);
    return;
}
if (migrateOnly) return;

app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseCors("frontend");
app.UseMiddleware<AuthBrowserGuard>();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
if (app.Environment.IsDevelopment()) app.MapOpenApi().AllowAnonymous();
app.MapControllers();
app.Run();

public partial class Program;
