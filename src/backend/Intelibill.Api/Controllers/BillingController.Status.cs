using Intelibill.Application.Features.Billing;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

/// <summary>
/// Observe-only licensing status. The shop read is for any live member of the active shop; the account read is
/// for billing members, with or without any shop. Neither result gates a merchant action.
/// </summary>
public sealed partial class BillingController
{
    [HttpGet("/api/licensing/shops/{shopId:guid}/status")]
    public async Task<IActionResult> ShopStatus(Guid shopId, [FromServices] IMerchantSubscriptionStatus status,
        CancellationToken ct)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();
        var result = await status.ReadShopAsync(caller, shopId, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpGet("{accountId:guid}/subscriptions")]
    public async Task<IActionResult> AccountSubscriptions(Guid accountId, [FromQuery] Guid? after,
        [FromServices] IMerchantSubscriptionStatus status, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();
        var result = await status.ReadAccountAsync(caller, accountId, after, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }
}
