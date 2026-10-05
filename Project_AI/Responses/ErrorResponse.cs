using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Project_AI.Application.Common.Enums;

namespace Project_AI.API.Responses;

public static class ErrorResponse
{
    public static ProblemDetails Create(HttpContext context, ErrorCode code, string? detail = null,
        IReadOnlyDictionary<string, string[]>? errors = null, HttpStatusCode? status = null)
    {
        var responseStatus = status ?? ResponseCodes.StatusFor(code);
        var problem = new ProblemDetails
        {
            Status = (int)responseStatus,
            Title = ResponseCodes.TitleFor(responseStatus),
            Detail = detail ?? ResponseCodes.MessageFor(code),
            Type = "urn:inboxagent:error:" + ResponseCodes.CodeFor(code),
            Instance = context.Request.Path.Value
        };
        problem.Extensions["code"] = ResponseCodes.CodeFor(code);
        problem.Extensions["traceId"] = GetTraceId(context);
        if (errors is not null) problem.Extensions["errors"] = errors;
        return problem;
    }

    public static Task WriteAsync(HttpContext context, ProblemDetails problem)
    {
        context.Response.Headers.CacheControl = "no-store";
        return Results.Problem(problem).ExecuteAsync(context);
    }

    public static Task WriteAsync(HttpContext context, ErrorCode code) => WriteAsync(context, Create(context, code));

    public static Task WriteStatusAsync(HttpContext context)
    {
        var status = (HttpStatusCode)context.Response.StatusCode;
        return WriteAsync(context, Create(context, ResponseCodes.ErrorFor(status), status: status));
    }

    public static void Enrich(HttpContext context, ProblemDetails problem)
    {
        var status = (HttpStatusCode)(problem.Status ?? context.Response.StatusCode);
        problem.Extensions.TryAdd("code", ResponseCodes.CodeFor(status));
        problem.Extensions.TryAdd("traceId", GetTraceId(context));
        problem.Title ??= ResponseCodes.TitleFor(status);
        problem.Detail ??= ResponseCodes.MessageFor(ResponseCodes.ErrorFor(status));
        problem.Type ??= "urn:inboxagent:error:" + problem.Extensions["code"];
        problem.Instance ??= context.Request.Path.Value;
        context.Response.Headers.CacheControl = "no-store";
    }

    private static string GetTraceId(HttpContext context) => Activity.Current?.Id ?? context.TraceIdentifier;
}
