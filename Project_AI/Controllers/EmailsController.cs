using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Project_AI.API.Models;
using Project_AI.API.Security;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Emails;
using Project_AI.Application.Interfaces;

namespace Project_AI.API.Controllers;

[ApiController]
[Route("api/mailboxes/{mailboxId:guid}")]
public sealed class EmailsController : ControllerBase
{
    private readonly IEmailSyncService _sync;
    private readonly IEmailSyncStore _syncStore;
    private readonly IEmailQueryStore _emails;

    public EmailsController(IEmailSyncService sync, IEmailSyncStore syncStore, IEmailQueryStore emails)
    {
        _sync = sync;
        _syncStore = syncStore;
        _emails = emails;
    }

    [HttpPost("sync"), EnableRateLimiting("auth")]
    public Task<MailboxSyncResult> Sync(Guid mailboxId, CancellationToken cancellationToken) =>
        _sync.SyncAsync(CurrentUserId(), mailboxId, cancellationToken);

    [HttpGet("sync")]
    public Task<MailboxSyncStatus> Status(Guid mailboxId, CancellationToken cancellationToken) =>
        _syncStore.GetStatusAsync(CurrentUserId(), mailboxId, cancellationToken);

    [HttpGet("emails")]
    public Task<EmailPage> List(Guid mailboxId, [FromQuery] EmailListQuery query, CancellationToken cancellationToken) =>
        _emails.ListAsync(CurrentUserId(), mailboxId, query.Page, query.PageSize, query.UnreadOnly, cancellationToken);

    [HttpGet("emails/{emailId:guid}")]
    public Task<EmailDetail> Detail(Guid mailboxId, Guid emailId, CancellationToken cancellationToken) =>
        _emails.GetAsync(CurrentUserId(), mailboxId, emailId, cancellationToken);

    private Guid CurrentUserId() => AccessTokenClaims.Read(User)?.UserId
        ?? throw new AppException(ErrorCode.InvalidSession, "The InboxAgent session is no longer valid.");
}
