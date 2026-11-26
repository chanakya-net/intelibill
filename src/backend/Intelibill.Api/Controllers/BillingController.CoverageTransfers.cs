using Intelibill.Api.Middleware.RateLimiting;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

public sealed partial class BillingController
{
    private const string TransferRequestRoute = "{accountId:guid}/subscriptions/{termId:guid}/coverage-transfers";
    private const string TransferRoute = "{accountId:guid}/coverage-transfers/{transferId:guid}";
    private const string TransferShopRoute = "{accountId:guid}/coverage-transfer-shops/{shopId:guid}/{transferId:guid}/consent";

    [HttpPost(TransferRequestRoute + "/preview")]
    [RequestSizeLimit(CoverageTransfersV1.MaxRequestBytes)]
    [RateLimit(Limit = 60, PeriodInMinutes = 60, BackoffMinutes = 5, FailClosed = true)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> PreviewCoverageTransfer(Guid accountId, Guid termId, PreviewCoverageTransferV1 query,
        [FromServices] IMerchantCoverageTransfers transfers, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller) return SessionInvalid();
        var result = await transfers.PreviewCoverageTransferAsync(caller, accountId, termId, query, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpPost(TransferRequestRoute)]
    [RequestSizeLimit(CoverageTransfersV1.MaxRequestBytes)]
    [RateLimit(Limit = 30, PeriodInMinutes = 60, BackoffMinutes = 5, FailClosed = true)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> RequestCoverageTransfer(Guid accountId, Guid termId, RequestCoverageTransferV1 command,
        [FromServices] IMerchantCoverageTransfers transfers, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller) return SessionInvalid();
        var result = await transfers.RequestCoverageTransferAsync(caller, accountId, termId, command, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpGet(TransferRequestRoute + "/operations/{key:guid}")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> RecoverCoverageTransfer(Guid accountId, Guid termId, Guid key,
        [FromServices] IMerchantCoverageTransfers transfers, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller) return SessionInvalid();
        var result = await transfers.RecoverCoverageTransferAsync(caller, accountId, termId, key, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpGet("{accountId:guid}/coverage-transfers")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> CoverageTransfers(Guid accountId,
        [FromServices] IMerchantCoverageTransfers transfers, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller) return SessionInvalid();
        var result = await transfers.CoverageTransfersAsync(caller, accountId, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpGet(TransferRoute)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> CoverageTransfer(Guid accountId, Guid transferId,
        [FromServices] IMerchantCoverageTransfers transfers, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller) return SessionInvalid();
        var result = await transfers.CoverageTransferAsync(caller, accountId, transferId, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpPost(TransferRoute + "/consents/{purpose}/{decision}")]
    [RequestSizeLimit(1024)]
    [RateLimit(Limit = 30, PeriodInMinutes = 15, BackoffMinutes = 5, FailClosed = true)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> DecideCoverageTransfer(Guid accountId, Guid transferId, string purpose, string decision,
        CoverageTransferDecisionV1 command, [FromServices] IMerchantCoverageTransfers transfers, CancellationToken ct)
    {
        if (CoverageTransfersV1.BillingPurpose(purpose) is not { } bound || CoverageTransfersV1.Decision(decision) is not { } kind)
            return NotFound();
        if (ReadCaller() is not { } caller) return SessionInvalid();
        var result = await transfers.DecideCoverageTransferAsync(caller, accountId, transferId, bound, kind, command, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpGet(TransferRoute + "/consents/{purpose}/operations/{key:guid}")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> RecoverCoverageTransferDecision(Guid accountId, Guid transferId, string purpose, Guid key,
        [FromServices] IMerchantCoverageTransfers transfers, CancellationToken ct)
    {
        if (CoverageTransfersV1.BillingPurpose(purpose) is not { } bound) return NotFound();
        if (ReadCaller() is not { } caller) return SessionInvalid();
        var result = await transfers.RecoverCoverageTransferDecisionAsync(caller, accountId, transferId, bound, key, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpGet(TransferShopRoute)]
    [LicenceAction(LicenceAction.Billing)]
    public Task<IActionResult> ReadCoverageTransferShopConsent(Guid accountId, Guid shopId, Guid transferId,
        [FromServices] IMerchantCoverageTransfers transfers, CancellationToken ct) =>
        TransferShopConsentAsync(accountId, shopId, transferId, CoverageTransferShopAction.Read, null, transfers, ct);

    [HttpPost(TransferShopRoute + "/{decision}")]
    [RequestSizeLimit(1024)]
    [RateLimit(Limit = 30, PeriodInMinutes = 15, BackoffMinutes = 5, FailClosed = true)]
    [LicenceAction(LicenceAction.Billing)]
    public Task<IActionResult> ChangeCoverageTransferShopConsent(Guid accountId, Guid shopId, Guid transferId, string decision,
        CoverageTransferDecisionV1 command, [FromServices] IMerchantCoverageTransfers transfers, CancellationToken ct) =>
        decision switch
        {
            "accept" => TransferShopConsentAsync(accountId, shopId, transferId, CoverageTransferShopAction.Accept, command, transfers, ct),
            "revoke" => TransferShopConsentAsync(accountId, shopId, transferId, CoverageTransferShopAction.Revoke, command, transfers, ct),
            _ => Task.FromResult<IActionResult>(NotFound()),
        };

    private async Task<IActionResult> TransferShopConsentAsync(Guid account, Guid shop, Guid transfer,
        CoverageTransferShopAction action, CoverageTransferDecisionV1? decision, IMerchantCoverageTransfers transfers,
        CancellationToken ct)
    {
        if (ReadCaller() is not { } caller) return SessionInvalid();
        var result = await transfers.CoverageTransferShopConsentAsync(caller, account, shop, transfer, action, decision, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }
}
