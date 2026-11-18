using Intelibill.Application.Common.Licensing;
using Intelibill.Api.Middleware.RateLimiting;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

public sealed partial class BillingController
{
    [HttpPost("{accountId:guid}/ownership-transfers")]
    [RequestSizeLimit(6144)]
    [RateLimit(Limit = 20, PeriodInMinutes = 60, BackoffMinutes = 15, FailClosed = true)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> InitiateOwnershipTransfer(Guid accountId, [FromBody] MerchantOwnershipTransferRequest request,
        [FromServices] IMerchantBillingOwnershipTransfers transfers, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();
        var result = await transfers.InitiateAsync(caller, accountId, request, ct);
        return result.IsError ? Failure(result.FirstError) : Accepted(result.Value);
    }

    [HttpGet("/api/billing/ownership-transfers")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> OwnershipInbox([FromQuery] Guid? after,
        [FromServices] IMerchantBillingOwnershipTransfers transfers, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();
        var result = await transfers.TransferInboxAsync(caller, after, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpGet("/api/billing/ownership-transfers/{transfer:guid}")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> OwnershipPreview(Guid transfer,
        [FromServices] IMerchantBillingOwnershipTransfers transfers, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();
        var result = await transfers.TransferPreviewAsync(caller, transfer, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpPost("/api/billing/ownership-transfers/{transfer:guid}/accept")]
    [RequestSizeLimit(1024)]
    [RateLimit(Limit = 30, PeriodInMinutes = 15, BackoffMinutes = 5, FailClosed = true)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> AcceptOwnershipTransfer(Guid transfer, [FromBody] AcceptBillingOwnershipTransferV1 request,
        [FromServices] IMerchantBillingOwnershipTransfers transfers, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();
        var result = await transfers.AcceptTransferAsync(caller, transfer, request, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpGet("/api/billing/ownership-transfer-operations/{operation}/{key:guid}")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> RecoverOwnershipTransfer(string operation, Guid key,
        [FromServices] IMerchantBillingOwnershipTransfers transfers, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();
        if (operation == "issue")
        {
            var result = await transfers.RecoverTransferIssueAsync(caller, key, ct);
            return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
        }
        if (operation == "accept")
        {
            var result = await transfers.RecoverTransferAcceptAsync(caller, key, ct);
            return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
        }
        return NotFound();
    }
}
