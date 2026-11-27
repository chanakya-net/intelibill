using Intelibill.Application.Common.Licensing;
using Intelibill.Api.Middleware.RateLimiting;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

public sealed partial class BillingController
{
    private const string SlotRoute = "{accountId:guid}/subscriptions/{termId:guid}/slot-assignments";

    private const string SlotConsentRoute = "{accountId:guid}/slot-assignments/{shopId:guid}/{assignmentId:guid}/consent";

    [HttpGet(SlotConsentRoute)]
    [LicenceAction(LicenceAction.Billing)]
    public Task<IActionResult> ReadSlotConsent(Guid accountId, Guid shopId, Guid assignmentId, [FromServices] IMerchantCoverageSlots slots,
        CancellationToken ct) => SlotDecisionAsync(accountId, shopId, assignmentId, ShopAffiliationConsentAction.Read, Guid.Empty, slots, ct);

    [HttpPost(SlotConsentRoute + "/{decision}")]
    [RequestSizeLimit(1024)]
    [RateLimit(Limit = 30, PeriodInMinutes = 15, BackoffMinutes = 5, FailClosed = true)]
    [LicenceAction(LicenceAction.Billing)]
    public Task<IActionResult> ChangeSlotConsent(Guid accountId, Guid shopId, Guid assignmentId, string decision,
        AffiliationOperation operation, [FromServices] IMerchantCoverageSlots slots, CancellationToken ct)
    {
        var action = decision switch
        {
            "grant" => ShopAffiliationConsentAction.Grant,
            "accept" => ShopAffiliationConsentAction.Accept,
            "revoke" => ShopAffiliationConsentAction.Revoke,
            _ => ShopAffiliationConsentAction.Read,
        };
        return action == ShopAffiliationConsentAction.Read ? Task.FromResult<IActionResult>(NotFound()) :
            SlotDecisionAsync(accountId, shopId, assignmentId, action, operation.OperationKey, slots, ct);
    }

    private async Task<IActionResult> SlotDecisionAsync(Guid account, Guid shop, Guid assignment, ShopAffiliationConsentAction action,
        Guid key, IMerchantCoverageSlots slots, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller) return SessionInvalid();
        var result = await slots.SlotConsentAsync(caller, account, shop, assignment, action, key, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpGet(SlotRoute)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> CoverageSlots(Guid accountId, Guid termId, [FromServices] IMerchantCoverageSlots slots,
        CancellationToken ct, [FromQuery] int page = 1)
    {
        if (ReadCaller() is not { } caller) return SessionInvalid();
        var result = await slots.SlotsAsync(caller, accountId, termId, page, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpPost(SlotRoute)]
    [RequestSizeLimit(1024)]
    [RateLimit(Limit = 30, PeriodInMinutes = 60, BackoffMinutes = 5, FailClosed = true)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> AssignCoverageSlot(Guid accountId, Guid termId, AssignUnusedCoverageSlotV1 command,
        [FromServices] IMerchantCoverageSlots slots, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller) return SessionInvalid();
        var result = await slots.AssignAsync(caller, accountId, termId, command, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpGet(SlotRoute + "/operations/{key:guid}")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> RecoverCoverageSlot(Guid accountId, Guid termId, Guid key,
        [FromServices] IMerchantCoverageSlots slots, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller) return SessionInvalid();
        var result = await slots.RecoverSlotAsync(caller, accountId, termId, key, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }
}
