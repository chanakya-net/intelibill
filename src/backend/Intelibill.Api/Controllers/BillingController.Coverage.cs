using Intelibill.Application.Common.Licensing;
using System.Text.Json.Serialization;
using Intelibill.Api.Middleware.RateLimiting;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

public sealed partial class BillingController
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record AffiliationOperation(Guid OperationKey);

    [HttpGet("{accountId:guid}/affiliations")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> AffiliationList(Guid accountId, [FromServices] IMerchantShopAffiliations affiliations,
        CancellationToken ct)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();
        var result = await affiliations.ListAsync(caller, accountId, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpPost("{accountId:guid}/affiliations")]
    [RequestSizeLimit(1024)]
    [RateLimit(Limit = 30, PeriodInMinutes = 60, BackoffMinutes = 5, FailClosed = true)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> RequestAffiliation(Guid accountId, [FromBody] RequestShopAffiliationV1 request,
        [FromServices] IMerchantShopAffiliations affiliations, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();
        var result = await affiliations.RequestAsync(caller, accountId, request, ct);
        return result.IsError ? Failure(result.FirstError) : StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpGet("{accountId:guid}/affiliations/{shopId:guid}/{requestId:guid}/consent")]
    [LicenceAction(LicenceAction.Billing)]
    public Task<IActionResult> ReadAffiliationConsent(Guid accountId, Guid shopId, Guid requestId,
        [FromServices] IMerchantShopAffiliations affiliations, CancellationToken ct) =>
        RunConsentAsync(accountId, shopId, requestId, ShopAffiliationConsentAction.Read, Guid.Empty, affiliations, ct);

    [HttpPost("{accountId:guid}/affiliations/{shopId:guid}/{requestId:guid}/consent/{decision}")]
    [RequestSizeLimit(1024)]
    [RateLimit(Limit = 30, PeriodInMinutes = 15, BackoffMinutes = 5, FailClosed = true)]
    [LicenceAction(LicenceAction.Billing)]
    public Task<IActionResult> ChangeAffiliationConsent(Guid accountId, Guid shopId, Guid requestId, string decision,
        [FromBody] AffiliationOperation operation, [FromServices] IMerchantShopAffiliations affiliations, CancellationToken ct)
    {
        var action = decision switch
        {
            "grant" => ShopAffiliationConsentAction.Grant,
            "revoke" => ShopAffiliationConsentAction.Revoke,
            "accept" => ShopAffiliationConsentAction.Accept,
            _ => ShopAffiliationConsentAction.Read,
        };
        return action == ShopAffiliationConsentAction.Read ? Task.FromResult<IActionResult>(NotFound())
            : RunConsentAsync(accountId, shopId, requestId, action, operation.OperationKey, affiliations, ct);
    }

    private async Task<IActionResult> RunConsentAsync(Guid account, Guid shop, Guid request, ShopAffiliationConsentAction action,
        Guid operation, IMerchantShopAffiliations affiliations, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();
        var result = await affiliations.ConsentAsync(caller, account, shop, request, action, operation, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }
}
