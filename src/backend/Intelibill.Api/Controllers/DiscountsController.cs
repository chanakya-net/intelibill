using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Features.Discounts.Commands.CreateDiscountRule;
using Intelibill.Application.Features.Discounts.Commands.DisableDiscountRule;
using Intelibill.Application.Features.Discounts.Commands.ReplaceDiscountRule;
using Intelibill.Application.Features.Discounts.DTOs;
using Intelibill.Application.Features.Discounts.Queries.GetDiscountRuleDetail;
using Intelibill.Application.Features.Discounts.Queries.GetDiscountRules;
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

    [HttpGet]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> GetDiscountRules(
        [FromQuery] string? status,
        [FromQuery] DiscountRuleType? ruleType,
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<Intelibill.Application.Features.Expenses.DTOs.PaginatedList<DiscountRuleListItemDto>>>(
            new GetDiscountRulesQuery(UserId!.Value, ActiveShopId!.Value, status, ruleType, search, sort, page, pageSize),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> GetDiscountRule(Guid id, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<DiscountRuleDto>>(
            new GetDiscountRuleDetailQuery(UserId!.Value, ActiveShopId!.Value, id),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> CreateDiscountRule(
        [FromBody] CreateDiscountRuleRequest request,
        CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<DiscountRuleDto>>(
            new CreateDiscountRuleCommand(
                UserId!.Value,
                ActiveShopId!.Value,
                request.RuleType,
                request.Name,
                request.Description,
                request.InventoryBatchId,
                request.Percentage,
                request.ThresholdAmount,
                request.StartsAt,
                request.EndsAt,
                request.BelowCostConfirmed,
                request.BelowCostConfirmationReason),
            cancellationToken);

        return result.ToActionResult(rule => CreatedAtAction(nameof(GetDiscountRule), new { id = rule.Id }, rule));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> ReplaceDiscountRule(
        Guid id,
        [FromBody] ReplaceDiscountRuleRequest request,
        CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<DiscountRuleDto>>(
            new ReplaceDiscountRuleCommand(
                UserId!.Value,
                ActiveShopId!.Value,
                id,
                request.RuleType,
                request.Name,
                request.Description,
                request.InventoryBatchId,
                request.Percentage,
                request.ThresholdAmount,
                request.StartsAt,
                request.EndsAt,
                request.BelowCostConfirmed,
                request.BelowCostConfirmationReason,
                request.DisabledReason),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost("{id:guid}/disable")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> DisableDiscountRule(
        Guid id,
        [FromBody] DisableDiscountRuleRequest request,
        CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<DiscountRuleDto>>(
            new DisableDiscountRuleCommand(UserId!.Value, ActiveShopId!.Value, id, request.Reason),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

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

public sealed record CreateDiscountRuleRequest(
    DiscountRuleType RuleType,
    string Name,
    string? Description,
    Guid? InventoryBatchId,
    decimal Percentage,
    decimal? ThresholdAmount,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    bool BelowCostConfirmed,
    string? BelowCostConfirmationReason);

public sealed record ReplaceDiscountRuleRequest(
    DiscountRuleType RuleType,
    string Name,
    string? Description,
    Guid? InventoryBatchId,
    decimal Percentage,
    decimal? ThresholdAmount,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    bool BelowCostConfirmed,
    string? BelowCostConfirmationReason,
    string? DisabledReason);

public sealed record DisableDiscountRuleRequest(string? Reason);
