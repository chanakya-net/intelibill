using Intelibill.Application.Common.Licensing;
using Intelibill.Api.Middleware.RateLimiting;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

public sealed partial class BillingController
{
    [HttpPost("{accountId:guid}/members/{memberId:guid}/remove")]
    [RequestSizeLimit(BillingInvitationsV1.MaximumRemovalBodyBytes)]
    [RateLimit(Limit = 20, PeriodInMinutes = 60, BackoffMinutes = 15, FailClosed = true)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> RemoveManager(Guid accountId, Guid memberId,
        [FromBody] RemoveBillingManagerV1 request, [FromServices] IMerchantBillingInvitations invitations,
        CancellationToken cancellationToken)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();
        var result = await invitations.RemoveManagerAsync(caller, accountId, memberId, request, cancellationToken);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }
}
