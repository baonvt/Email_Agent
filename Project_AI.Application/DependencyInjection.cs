using Microsoft.Extensions.DependencyInjection;
using Project_AI.Application.Features.Auth;

namespace Project_AI.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        return services;
    }
}
