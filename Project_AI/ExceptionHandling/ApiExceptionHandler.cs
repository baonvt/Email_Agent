using System.Net;
using Microsoft.AspNetCore.Diagnostics;
using Project_AI.API.Responses;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;

namespace Project_AI.API.ExceptionHandling;

public sealed class ApiExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ApiExceptionHandler> _logger;

    public ApiExceptionHandler(ILogger<ApiExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (context.RequestAborted.IsCancellationRequested)
        {
            return true;
        }

        var error = exception as AppException;
        var code = error?.Code ?? ErrorCode.InternalError;
        var status = ResponseCodes.StatusFor(code);
        if (exception is BadHttpRequestException badRequest)
        {
            status = (HttpStatusCode)badRequest.StatusCode;
            code = ResponseCodes.ErrorFor(status);
        }

        var isServerError = (int)status >= 500;
        var problem = ErrorResponse.Create(context, code,
            detail: isServerError ? null : error?.Message,
            errors: isServerError ? null : error?.Errors, status: status);
        if (isServerError)
        {
            _logger.LogError("Request failed with {ErrorCode} ({ErrorType}), trace {TraceId}. Stack: {StackTrace}",
                code, exception.GetType().Name, problem.Extensions["traceId"], exception.StackTrace);
        }

        await ErrorResponse.WriteAsync(context, problem);
        return true;
    }
}
