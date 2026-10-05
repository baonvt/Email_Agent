using Microsoft.Extensions.Options;
using Project_AI.API.Options;
using Project_AI.API.Responses;
using Project_AI.Application.Common.Enums;

namespace Project_AI.API.Middleware;

public sealed class AuthBrowserGuard(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IOptions<AuthWebOptions> options)
    {
        if (context.GetEndpoint() is not null && context.Request.Path.StartsWithSegments("/api/auth"))
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Pragma = "no-cache";
            if (HttpMethods.IsPost(context.Request.Method))
            {
                var origin = context.Request.Headers.Origin.ToString();
                // Exact allowlist + a mandatory custom header forces a CORS preflight for cross-origin
                // requests. Browsers cannot forge Origin, and HTML forms cannot set this header.
                if (!options.Value.AllowedOrigins.Contains(origin, StringComparer.Ordinal)
                    || context.Request.Headers["X-InboxAgent-CSRF"] != "1")
                {
                    await ErrorResponse.WriteAsync(context, ErrorCode.CsrfRejected);
                    return;
                }
            }
        }
        await next(context);
    }
}
