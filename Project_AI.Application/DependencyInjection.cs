using Microsoft.Extensions.DependencyInjection;
using Project_AI.Application.Interfaces;
using Project_AI.Application.Services;

namespace Project_AI.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IMailboxConnectionService, MailboxConnectionService>();
        services.AddScoped<IEmailSyncService, EmailSyncService>();
        services.AddScoped<IEmailAnalysisService, EmailAnalysisService>();
        services.AddScoped<IReplyDraftService, ReplyDraftService>();
        services.AddScoped<IReplySendService, ReplySendService>();
        return services;
    }
}
