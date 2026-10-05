using Microsoft.AspNetCore.Authentication.JwtBearer;
using Project_AI.API.Responses;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Interfaces;

namespace Project_AI.API.Security;

public sealed class AuthJwtEvents : JwtBearerEvents
{
    private readonly IAuthSessionService _authSessionService;
    private readonly ILogger<AuthJwtEvents> _logger;

    public AuthJwtEvents(IAuthSessionService authSessionService, ILogger<AuthJwtEvents> logger)
    {
        _authSessionService = authSessionService;
        _logger = logger;
    }

    private const string Unavailable = "InboxAgent.AuthUnavailable";

    public override async Task TokenValidated(TokenValidatedContext context)
    {
        var token = AccessTokenClaims.Read(context.Principal!);
        if (token is null) { context.Fail("Missing session claims."); return; }
        try
        {
            if (!await _authSessionService.ValidateAsync(token, context.HttpContext.RequestAborted))
                context.Fail("The session is revoked or expired.");
        }
        catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Fail closed for both Redis and PostgreSQL outages. Never log token contents.
            context.HttpContext.Items[Unavailable] = true;
            _logger.LogWarning("Session validation unavailable ({ErrorType}).", ex.GetType().Name);
            context.Fail("Session validation is unavailable.");
        }
    }

    public override Task Challenge(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        var unavailable = context.HttpContext.Items.ContainsKey(Unavailable);
        if (!unavailable) context.Response.Headers.WWWAuthenticate = "Bearer";
        return ErrorResponse.WriteAsync(context.HttpContext,
            unavailable ? ErrorCode.AuthUnavailable : ErrorCode.Unauthorized);
    }

    public override Task Forbidden(ForbiddenContext context) =>
        ErrorResponse.WriteAsync(context.HttpContext, ErrorCode.Forbidden);
}
