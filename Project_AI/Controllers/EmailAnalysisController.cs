using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Project_AI.API.Security;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Analysis;
using Project_AI.Application.Interfaces;

namespace Project_AI.API.Controllers;

[ApiController]
[Route("api/mailboxes/{mailboxId:guid}/emails/{emailId:guid}/analysis")]
public sealed class EmailAnalysisController : ControllerBase
{
    private readonly IEmailAnalysisService _service;

    public EmailAnalysisController(IEmailAnalysisService service) { _service = service; }

    [HttpPost, EnableRateLimiting("auth")]
    public Task<EmailAnalysisResponse> Analyze(Guid mailboxId, Guid emailId, [FromQuery] bool force,
        CancellationToken cancellationToken) =>
        _service.AnalyzeAsync(CurrentUserId(), mailboxId, emailId, force, cancellationToken);

    [HttpGet]
    public Task<EmailAnalysisStatus> Status(Guid mailboxId, Guid emailId, CancellationToken cancellationToken) =>
        _service.GetAsync(CurrentUserId(), mailboxId, emailId, cancellationToken);

    private Guid CurrentUserId() => AccessTokenClaims.Read(User)?.UserId
        ?? throw new AppException(ErrorCode.InvalidSession, "The InboxAgent session is no longer valid.");
}
