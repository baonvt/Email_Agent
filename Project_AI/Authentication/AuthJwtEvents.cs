using Microsoft.AspNetCore.Authentication.JwtBearer;


namespace Project_AI.API.Authentication;

public sealed class AuthJwtEvents(IAuthSessions sessions, ILogger<AuthJwtEvents> logger) : JwtBearerEvents
{
    private const string Unavailable = "InboxAgent.AuthUnavailable";

    public override async Task TokenValidated(TokenValidatedContext context)
    {
        var token = AccessTokenClaims.Read(context.Principal!);
        if (token is null) { context.Fail("Missing session claims."); return; }
        try
        {
            if (!await sessions.ValidateAsync(token, context.HttpContext.RequestAborted))
                context.Fail("The session is revoked or expired.");
        }
        catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Fail closed for both Redis and PostgreSQL outages. Never log token contents.
            context.HttpContext.Items[Unavailable] = true;
            logger.LogWarning("Session validation unavailable ({ErrorType}).", ex.GetType().Name);
            context.Fail("Session validation is unavailable.");
        }
    }

    public override Task Challenge(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        var unavailable = context.HttpContext.Items.ContainsKey(Unavailable);
        if (!unavailable) context.Response.Headers.WWWAuthenticate = "Bearer";
        return Results.Problem(statusCode: unavailable ? 503 : 401,
            title: unavailable ? "Authentication unavailable" : "Authentication required",
            extensions: new Dictionary<string, object?> { ["code"] = unavailable ? "auth_unavailable" : "unauthorized" })
            .ExecuteAsync(context.HttpContext);
    }

    public override Task Forbidden(ForbiddenContext context) =>
        Results.Problem(statusCode: 403, title: "Access denied",
            extensions: new Dictionary<string, object?> { ["code"] = "forbidden" }).ExecuteAsync(context.HttpContext);
}
