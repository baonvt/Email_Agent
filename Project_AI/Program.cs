using Project_AI.API;
using Project_AI.API.Middleware;
using Project_AI.API.Responses;
using Project_AI.Application;
using Project_AI.Infrastructure;
using Project_AI.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration, builder.Environment)
    .AddPresentation(builder.Configuration, builder.Environment);

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
app.UseStatusCodePages(context => ErrorResponse.WriteStatusAsync(context.HttpContext));
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseRouting();
app.UseCors("frontend");
app.UseMiddleware<AuthBrowserGuard>();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
if (app.Environment.IsDevelopment()) app.MapOpenApi().AllowAnonymous();
app.MapControllers().RequireAuthorization();
app.MapHealthChecks("/health/ready").AllowAnonymous();
app.Run();

public partial class Program;
