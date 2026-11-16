using System.Text.Json.Serialization;
using Intelibill.Api.Middleware.RateLimiting;
using Intelibill.Application.Features.Billing;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

public sealed partial class BillingController
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record IssueInvitationRequest(Guid OperationKey, int ExpectedOwnerRevision, string? RecipientKind,
        string? Recipient)
    {
        public override string ToString() => "[REDACTED]";
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record AcceptInvitationRequest(int ExpectedInvitationRevision);

    [HttpPost("{accountId:guid}/invitations")]
    [RequestSizeLimit(2048)]
    // In-app delivery sends no credential or message. Use the caller's budget so a billing request
    // cannot consume the recipient's sign-in or account-recovery destination budget.
    [RateLimit(Limit = 20, PeriodInMinutes = 60, BackoffMinutes = 15, FailClosed = true)]
    public async Task<IActionResult> Invite(Guid accountId, [FromBody] IssueInvitationRequest request,
        [FromServices] IMerchantBillingInvitations invitations, CancellationToken cancellationToken)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();
        var result = await invitations.IssueAsync(caller, accountId,
            new(request.OperationKey, request.ExpectedOwnerRevision, request.RecipientKind, request.Recipient), cancellationToken);
        return result.IsError ? Failure(result.FirstError) : Accepted(result.Value);
    }

    [HttpGet("{accountId:guid}/members")]
    public async Task<IActionResult> Members(Guid accountId, [FromQuery] Guid? after,
        [FromServices] IMerchantBillingInvitations invitations, CancellationToken cancellationToken)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();
        var result = await invitations.RosterAsync(caller, accountId, after, cancellationToken);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpGet("/api/billing/invitations")]
    public async Task<IActionResult> Inbox([FromQuery] Guid? after,
        [FromServices] IMerchantBillingInvitations invitations, CancellationToken cancellationToken)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();
        var result = await invitations.InboxAsync(caller, after, cancellationToken);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpPost("/api/billing/invitations/{invitationId:guid}/accept")]
    [RequestSizeLimit(2048)]
    [RateLimit(Limit = 30, PeriodInMinutes = 15, BackoffMinutes = 5, FailClosed = true)]
    public async Task<IActionResult> Accept(Guid invitationId, [FromBody] AcceptInvitationRequest request,
        [FromServices] IMerchantBillingInvitations invitations, CancellationToken cancellationToken)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();
        var result = await invitations.AcceptAsync(caller, invitationId, request.ExpectedInvitationRevision, cancellationToken);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }
}
