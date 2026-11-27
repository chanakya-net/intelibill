using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Billing;
using Intelibill.Api.Middleware.RateLimiting;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

public sealed partial class BillingController
{
    private const string ReplacementRoute = "{accountId:guid}/subscriptions/{termId:guid}/coverage-replacements";

    [HttpGet(ReplacementRoute)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> CoverageReplacements(Guid accountId, Guid termId,
        [FromServices] IMerchantCoverageReplacements replacements, CancellationToken ct, [FromQuery] int page = 1)
    {
        if (ReadCaller() is not { } caller) return SessionInvalid();
        var result = await replacements.ReplacementsAsync(caller, accountId, termId, page, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpPost(ReplacementRoute)]
    [RequestSizeLimit(1024)]
    [RateLimit(Limit = 30, PeriodInMinutes = 60, BackoffMinutes = 5, FailClosed = true)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> ScheduleReplacement(Guid accountId, Guid termId, ScheduleCoverageReplacementV1 command,
        [FromServices] IMerchantCoverageReplacements replacements, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller) return SessionInvalid();
        var result = await replacements.ScheduleReplacementAsync(caller, accountId, termId, command, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpGet(ReplacementRoute + "/operations/{key:guid}")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> RecoverReplacement(Guid accountId, Guid termId, Guid key,
        [FromServices] IMerchantCoverageReplacements replacements, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller) return SessionInvalid();
        var result = await replacements.RecoverReplacementAsync(caller, accountId, termId, key, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    private const string ReplacementConsentRoute = "{accountId:guid}/coverage-replacements/{shopId:guid}/{replacementId:guid}/consent";

    [HttpGet(ReplacementConsentRoute)]
    [LicenceAction(LicenceAction.Billing)]
    public Task<IActionResult> ReadReplacementConsent(Guid accountId, Guid shopId, Guid replacementId, [FromServices] IMerchantCoverageReplacements changes,
        CancellationToken ct) => ReplacementDecisionAsync(accountId, shopId, replacementId, ShopAffiliationConsentAction.Read, Guid.Empty, changes, ct);

    [HttpPost(ReplacementConsentRoute + "/{decision}")]
    [RequestSizeLimit(1024)]
    [RateLimit(Limit = 30, PeriodInMinutes = 15, BackoffMinutes = 5, FailClosed = true)]
    [LicenceAction(LicenceAction.Billing)]
    public Task<IActionResult> ChangeReplacementConsent(Guid accountId, Guid shopId, Guid replacementId, string decision,
        AffiliationOperation operation, [FromServices] IMerchantCoverageReplacements changes, CancellationToken ct)
    {
        var action = decision switch
        {
            "grant" => ShopAffiliationConsentAction.Grant,
            "accept" => ShopAffiliationConsentAction.Accept,
            "revoke" => ShopAffiliationConsentAction.Revoke,
            _ => ShopAffiliationConsentAction.Read,
        };
        return action == ShopAffiliationConsentAction.Read ? Task.FromResult<IActionResult>(NotFound()) :
            ReplacementDecisionAsync(accountId, shopId, replacementId, action, operation.OperationKey, changes, ct);
    }

    private async Task<IActionResult> ReplacementDecisionAsync(Guid account, Guid shop, Guid replacement, ShopAffiliationConsentAction action,
        Guid key, IMerchantCoverageReplacements changes, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller) return SessionInvalid();
        var result = await changes.ReplacementConsentAsync(caller, account, shop, replacement, action, key, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }
}
