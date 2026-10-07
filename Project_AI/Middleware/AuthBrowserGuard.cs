using Microsoft.Extensions.Options;
using Project_AI.API.Options;
using Project_AI.API.Responses;
using Project_AI.Application.Common.Enums;

namespace Project_AI.API.Middleware;

public sealed class AuthBrowserGuard
{
    private readonly RequestDelegate _next;

    public AuthBrowserGuard(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IOptions<AuthWebOptions> options)
    {
        var isBrowserPath = context.Request.Path.StartsWithSegments("/api/auth")
            || context.Request.Path.StartsWithSegments("/api/mailboxes");
        if (context.GetEndpoint() is null || !isBrowserPath)
        {
            await _next(context);
            return;
        }

        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)
            && !HttpMethods.IsOptions(context.Request.Method))
        {
            var origin = context.Request.Headers.Origin.ToString();
            // Exact origin matching and a custom header force a CORS preflight in browsers.
            var isAllowedOrigin = options.Value.AllowedOrigins.Contains(origin, StringComparer.Ordinal);
            var hasCsrfHeader = context.Request.Headers["X-InboxAgent-CSRF"] == "1";
            if (!isAllowedOrigin || !hasCsrfHeader)
            {
                await ErrorResponse.WriteAsync(context, ErrorCode.CsrfRejected);
                return;
            }
        }

        await _next(context);
    }
}
