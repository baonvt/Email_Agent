using Microsoft.AspNetCore.Diagnostics;


namespace Project_AI.API.ExceptionHandling;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var error = exception as AuthException;
        if (error is null) logger.LogError("Request failed ({ErrorType}), trace {TraceId}.", exception.GetType().Name, context.TraceIdentifier);
        var status = error?.Kind switch
        {
            AuthErrorKind.InvalidInput => StatusCodes.Status400BadRequest,
            AuthErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            AuthErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };
        await Results.Problem(statusCode: status,
            title: error?.Message ?? "An unexpected error occurred.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = error?.Code ?? "internal_error", ["traceId"] = context.TraceIdentifier
            }).ExecuteAsync(context);
        return true;
    }
}
