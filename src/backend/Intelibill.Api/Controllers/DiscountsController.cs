using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Features.Discounts.DTOs;
using Intelibill.Application.Features.Discounts.Queries.PreviewDiscountRule;
using Intelibill.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/discounts")]
[Authorize]
public sealed class DiscountsController : AuthenticatedControllerBase
{
    public DiscountsController(IMessageBus bus) : base(bus) { }

    [HttpPost("preview")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> PreviewDiscountRule(
        [FromBody] PreviewDiscountRuleRequest request,
        CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<DiscountRulePreviewDto>>(
            new PreviewDiscountRuleQuery(
                UserId!.Value,
                ActiveShopId!.Value,
                request.RuleType,
                request.Percentage,
                request.ThresholdAmount,
                request.InventoryBatchId,
                request.StartsAt,
                request.EndsAt,
                request.BelowCostConfirmed),
            cancellationToken);

        return result.ToActionResult(Ok);
    }
}

public sealed record PreviewDiscountRuleRequest(
    DiscountRuleType RuleType,
    decimal Percentage,
    decimal? ThresholdAmount,
    Guid? InventoryBatchId,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    bool BelowCostConfirmed);
