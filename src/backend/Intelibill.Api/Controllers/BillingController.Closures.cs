using Intelibill.Api.Extensions;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

public sealed partial class BillingController
{
    [HttpGet("/api/licensing/shops/{shopId:guid}/closure-preview")]
    [LicenceAction(LicenceAction.History)]
    public async Task<IActionResult> ShopClosurePreview(Guid shopId, [FromServices] IMerchantShopClosures closures,
        CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (ReadCaller() is not { } caller)
            return SessionInvalid();
        var result = await closures.PreviewAsync(caller, shopId, ct);
        if (result.IsError)
            return Failure(result.FirstError);
        if (HttpContext.Features.Get<IHttpResponseBodyFeature>() is SuspensionResponseBodyFeature guarded)
            guarded.RequireOwner(caller.AccessExpiry, result.Value.OwnerVersion);
        return Ok(result.Value);
    }

    [HttpPost("/api/licensing/shops/{shopId:guid}/closure")]
    [RequestSizeLimit(ShopClosuresV1.BodyLimit)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> CloseShop(Guid shopId, [FromBody] ShopClosureConfirmationV1 confirmation,
        [FromServices] IMerchantShopClosures closures, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (ReadCaller() is not { } caller)
            return SessionInvalid();
        var result = await closures.CloseAsync(caller, shopId, confirmation, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }
}
