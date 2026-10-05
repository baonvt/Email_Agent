using System.Net;
using Microsoft.AspNetCore.Diagnostics;
using Project_AI.API.Responses;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;

namespace Project_AI.API.ExceptionHandling;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (context.RequestAborted.IsCancellationRequested) return true;
        var error = exception as AppException;
        var code = error?.Code ?? ErrorCode.InternalError;
        var status = error is not null ? ResponseCodes.StatusFor(code)
            : exception is BadHttpRequestException badRequest
                ? (HttpStatusCode)badRequest.StatusCode : HttpStatusCode.InternalServerError;
        if (exception is BadHttpRequestException) code = ResponseCodes.ErrorFor(status);
        if ((int)status >= 500)
            logger.LogError("Request failed with {ErrorCode} ({ErrorType}), trace {TraceId}. Stack: {StackTrace}",
                code, exception.GetType().Name, context.TraceIdentifier, exception.StackTrace);
        await ErrorResponse.WriteAsync(context, ErrorResponse.Create(context, code,
            detail: (int)status < 500 ? error?.Message : null,
            errors: (int)status < 500 ? error?.Errors : null, status: status));
        return true;
    }
}
