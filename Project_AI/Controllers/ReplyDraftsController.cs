using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Project_AI.API.Models.Replies;
using Project_AI.API.Security;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Auth;
using Project_AI.Application.DTOs.Replies;
using Project_AI.Application.Interfaces;

namespace Project_AI.API.Controllers;

[ApiController, RequestSizeLimit(64 * 1024)]
[Route("api/mailboxes/{mailboxId:guid}")]
public sealed class ReplyDraftsController : ControllerBase
{
    private readonly IReplyDraftService _generator;
    private readonly IReplyDraftStore _drafts;
    private readonly IReplySendService _sender;
    public ReplyDraftsController(IReplyDraftService generator, IReplyDraftStore drafts, IReplySendService sender)
    {
        _generator = generator; _drafts = drafts; _sender = sender;
    }

    [HttpPost("emails/{emailId:guid}/draft"), EnableRateLimiting("auth")]
    public Task<ReplyDraftResponse> Generate(Guid mailboxId, Guid emailId, GenerateReplyRequest request, CancellationToken cancellationToken) =>
        _generator.GenerateAsync(Session().UserId, mailboxId, emailId, request.Force, request.ExpectedVersion, cancellationToken);

    [HttpGet("emails/{emailId:guid}/drafts")]
    public Task<IReadOnlyList<ReplyDraftResponse>> List(Guid mailboxId, Guid emailId, CancellationToken cancellationToken) =>
        _drafts.ListAsync(Session().UserId, mailboxId, emailId, cancellationToken);

    [HttpGet("drafts/{draftId:guid}")]
    public Task<ReplyDraftResponse> Get(Guid mailboxId, Guid draftId, CancellationToken cancellationToken) =>
        _drafts.GetAsync(Session().UserId, mailboxId, draftId, cancellationToken);

    [HttpPut("drafts/{draftId:guid}")]
    public Task<ReplyDraftResponse> Edit(Guid mailboxId, Guid draftId, EditReplyRequest request, CancellationToken cancellationToken) =>
        _drafts.EditAsync(Session().UserId, mailboxId, draftId, request.ExpectedVersion!.Value, request.BodyText, cancellationToken);

    [HttpDelete("drafts/{draftId:guid}")]
    public async Task<IActionResult> Delete(Guid mailboxId, Guid draftId, [FromQuery] Guid expectedVersion, CancellationToken cancellationToken)
    {
        await _drafts.DeleteAsync(Session().UserId, mailboxId, draftId, expectedVersion, cancellationToken);
        return NoContent();
    }

    [HttpPost("drafts/{draftId:guid}/send"), EnableRateLimiting("auth")]
    public Task<ReplyDraftResponse> Send(Guid mailboxId, Guid draftId, SendReplyRequest request, CancellationToken cancellationToken)
    {
        if (!request.ConfirmSend)
            throw new AppException(ErrorCode.InvalidInput, "Review the recipient and body, then explicitly confirm sending.");
        return _sender.SendAsync(Session(), mailboxId, draftId, request.ExpectedVersion!.Value, cancellationToken);
    }

    private AccessTokenContext Session() => AccessTokenClaims.Read(User)
        ?? throw new AppException(ErrorCode.InvalidSession, "The InboxAgent session is no longer valid.");
}
