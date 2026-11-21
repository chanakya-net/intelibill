using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

public sealed partial class BillingController
{
    [HttpGet("{accountId:guid}/closures")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> AccountClosure(Guid accountId, [FromServices] IMerchantShopClosures closures, CancellationToken ct) =>
        ReadCaller() is { } caller ? ClosureResponse(await closures.ReadProposalAsync(caller, accountId, ct)) : SessionInvalid();

    [HttpPost("{accountId:guid}/closures")]
    [RequestSizeLimit(ShopClosuresV1.BodyLimit)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> ProposeClosure(Guid accountId, [FromBody] ProposeAccountClosureV1 input,
        [FromServices] IMerchantShopClosures closures, CancellationToken ct) =>
        ReadCaller() is { } caller ? ClosureResponse(await closures.ProposeAsync(caller, accountId, input, ct)) : SessionInvalid();

    [HttpPost("{accountId:guid}/closures/{proposal:guid}/confirm")]
    [RequestSizeLimit(ShopClosuresV1.BodyLimit)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> ConfirmClosure(Guid accountId, Guid proposal, [FromBody] ConfirmAccountClosureV1 input,
        [FromServices] IMerchantShopClosures closures, CancellationToken ct) =>
        ReadCaller() is { } caller ? ClosureResponse(await closures.ConfirmAccountAsync(caller, accountId, proposal, input, ct)) : SessionInvalid();

    [HttpGet("/api/licensing/shops/{shopId:guid}/account-closure")]
    [LicenceAction(LicenceAction.History)]
    public async Task<IActionResult> ShopClosureProposal(Guid shopId, [FromServices] IMerchantShopClosures closures, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller) return SessionInvalid();
        var result = await closures.ReadOwnerProposalAsync(caller, shopId, ct);
        if (result.IsError) return ClosureResponse(result);
        if (HttpContext.Features.Get<IHttpResponseBodyFeature>() is SuspensionResponseBodyFeature guarded)
            guarded.RequireOwner(caller.AccessExpiry, result.Value.OwnerVersion);
        return ClosureResponse<OwnerClosureProposalV1>(result.Value.Proposal);
    }

    [HttpPost("/api/licensing/shops/{shopId:guid}/account-closure/{proposal:guid}")]
    [RequestSizeLimit(ShopClosuresV1.BodyLimit)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> ConsentToClosure(Guid shopId, Guid proposal, [FromBody] ShopClosureConfirmationV1 input,
        [FromServices] IMerchantShopClosures closures, CancellationToken ct) =>
        ReadCaller() is { } caller ? ClosureResponse(await closures.RecordConsentAsync(caller, shopId, proposal, input, ct)) : SessionInvalid();

    private IActionResult ClosureResponse<T>(ErrorOr<T> result)
    {
        Response.Headers.CacheControl = "no-store";
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }
}
