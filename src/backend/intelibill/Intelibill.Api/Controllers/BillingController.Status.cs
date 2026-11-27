using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Web.PublicErrors;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

/// <summary>
/// Observe-only licensing status. The shop read is for any live member of the active shop; the account read is
/// for billing members, with or without any shop. Neither result gates a merchant action.
/// </summary>
public sealed partial class BillingController
{
    [HttpGet("/api/licensing/shops/{shopId:guid}/status")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> ShopStatus(Guid shopId, [FromServices] IMerchantSubscriptionStatus status,
        CancellationToken ct)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();
        var result = await status.ReadShopAsync(caller, shopId, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpGet("/api/licensing/shops/{shopId:guid}/trading-authority")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> TradingAuthority(Guid shopId, [FromServices] IShopTradingAuthorityReader reader,
        CancellationToken ct)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();
        var result = await reader.ReadAsync(caller.Session, shopId, ct);
        if (!result.IsError)
            return Ok(result.Value);

        var failure = Failure(result.FirstError);
        if (result.FirstError.Metadata?.GetValueOrDefault("enforced") is bool enforced &&
            failure.Value is PublicError problem)
        {
            var details = new ProblemDetails
            {
                Status = problem.Status,
                Title = problem.Title,
                Type = problem.Type,
                Extensions = { ["code"] = problem.Code, ["traceId"] = problem.TraceId, ["enforced"] = enforced },
            };
            failure.Value = details;
        }

        return failure;
    }

    [HttpGet("{accountId:guid}/subscriptions")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> AccountSubscriptions(Guid accountId, [FromQuery] Guid? after,
        [FromServices] IMerchantSubscriptionStatus status, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();
        var result = await status.ReadAccountAsync(caller, accountId, after, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }
}
