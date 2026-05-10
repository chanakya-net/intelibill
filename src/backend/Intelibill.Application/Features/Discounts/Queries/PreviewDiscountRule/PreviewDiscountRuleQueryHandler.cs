using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Discounts.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Discounts.Queries.PreviewDiscountRule;

public sealed class PreviewDiscountRuleQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    IDiscountRuleRepository discountRuleRepository)
{
    private const int SampleCap = 5;

    public async Task<ErrorOr<DiscountRulePreviewDto>> Handle(
        PreviewDiscountRuleQuery query,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(query.ActorUserId, cancellationToken);
        if (user is null)
            return Error.NotFound("User.NotFound", "User not found.");

        var shop = await shopRepository.GetByIdAsync(query.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var membership = await shopRepository.GetMembershipAsync(query.ActorUserId, query.ShopId, cancellationToken);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        var errors = new List<DiscountRulePreviewMessageDto>();
        var infos = new List<DiscountRulePreviewMessageDto>();

        if (query.Percentage <= 0 || query.Percentage > 100)
        {
            errors.Add(new("discount.error.percent_out_of_range", "Percentage must be greater than 0 and at most 100."));
            return new DiscountRulePreviewDto(0, [], [], null, errors, infos);
        }

        if (query.StartsAt.HasValue && query.EndsAt.HasValue && query.EndsAt <= query.StartsAt)
        {
            errors.Add(new("discount.error.window_invalid", "EndsAt must be after StartsAt."));
            return new DiscountRulePreviewDto(0, [], [], null, errors, infos);
        }

        return query.RuleType switch
        {
            DiscountRuleType.BatchPercentage =>
                await PreviewBatchRuleAsync(query, errors, infos, cancellationToken),
            DiscountRuleType.SalePercentage or DiscountRuleType.SaleThresholdPercentage =>
                await PreviewSaleLevelRuleAsync(query, errors, infos, cancellationToken),
            _ => Errors.Discount.UnsupportedRuleType(query.RuleType)
        };
    }

    private async Task<ErrorOr<DiscountRulePreviewDto>> PreviewBatchRuleAsync(
        PreviewDiscountRuleQuery query,
        List<DiscountRulePreviewMessageDto> errors,
        List<DiscountRulePreviewMessageDto> infos,
        CancellationToken cancellationToken)
    {
        if (!query.InventoryBatchId.HasValue)
        {
            errors.Add(new("discount.error.batch_required", "Batch ID is required for batch percentage rules."));
            return new DiscountRulePreviewDto(0, [], [], null, errors, infos);
        }

        var batch = await inventoryBatchRepository.GetByIdWithItemAsync(
            query.InventoryBatchId.Value, query.ShopId, cancellationToken);

        if (batch is null)
        {
            errors.Add(new("discount.error.batch_not_found", "Inventory batch was not found."));
            return new DiscountRulePreviewDto(0, [], [], null, errors, infos);
        }

        if (batch.IsVoided)
            errors.Add(new("discount.error.batch_voided", "Cannot apply a discount rule to a voided batch."));

        var existingRules = await discountRuleRepository.GetAllActiveByBatchAsync(
            query.ShopId, query.InventoryBatchId.Value, cancellationToken);

        var overlapping = existingRules
            .Where(r => WindowsOverlap(r.StartsAt, r.EndsAt, query.StartsAt, query.EndsAt))
            .ToList();

        if (overlapping.Count > 0)
            errors.Add(new("discount.error.overlap",
                $"This batch already has {overlapping.Count} active discount rule(s) with an overlapping time window."));

        var discountedPrice = batch.SalesPrice * (1m - query.Percentage / 100m);
        var isBelowCost = discountedPrice < batch.CostPrice;
        var safeMaxPct = ComputeSafeMax(batch.SalesPrice, batch.CostPrice);
        var batchDto = ToBatchDto(batch, discountedPrice);

        if (isBelowCost && !query.BelowCostConfirmed)
            errors.Add(new("discount.error.below_cost_confirmation_required",
                "This discount would result in selling below cost. Confirm with belowCostConfirmed=true to proceed."));

        var affectedCount = batch.IsVoided ? 0 : 1;
        IReadOnlyList<DiscountRulePreviewBatchDto> affectedSample = batch.IsVoided ? [] : [batchDto];
        IReadOnlyList<DiscountRulePreviewBatchDto> belowCostSample = isBelowCost && !batch.IsVoided ? [batchDto] : [];

        return new DiscountRulePreviewDto(affectedCount, affectedSample, belowCostSample, safeMaxPct, errors, infos);
    }

    private async Task<ErrorOr<DiscountRulePreviewDto>> PreviewSaleLevelRuleAsync(
        PreviewDiscountRuleQuery query,
        List<DiscountRulePreviewMessageDto> errors,
        List<DiscountRulePreviewMessageDto> infos,
        CancellationToken cancellationToken)
    {
        if (query.RuleType == DiscountRuleType.SaleThresholdPercentage)
        {
            if (!query.ThresholdAmount.HasValue || query.ThresholdAmount.Value <= 0)
            {
                errors.Add(new("discount.error.threshold_required",
                    "ThresholdAmount is required and must be positive for threshold-based rules."));
                return new DiscountRulePreviewDto(0, [], [], null, errors, infos);
            }
        }

        var allBatches = await inventoryBatchRepository.GetByShopAsync(query.ShopId, cancellationToken);
        var sellable = allBatches.Where(b => !b.IsVoided && b.Quantity > 0).ToList();

        if (sellable.Count == 0)
        {
            infos.Add(new("discount.info.no_affected_batches", "No active sellable batches found in this shop."));
            return new DiscountRulePreviewDto(0, [], [], null, errors, infos);
        }

        var batchDtos = sellable
            .Select(b => ToBatchDto(b, b.SalesPrice * (1m - query.Percentage / 100m)))
            .ToList();

        var belowCostDtos = batchDtos.Where(b => b.DiscountedPrice < b.CostPrice).ToList();

        decimal? safeMaxPct = sellable.Any(b => b.SalesPrice > b.CostPrice)
            ? sellable
                .Where(b => b.SalesPrice > b.CostPrice)
                .Select(b => ComputeSafeMax(b.SalesPrice, b.CostPrice))
                .Min()
            : null;

        if (belowCostDtos.Count > 0 && !query.BelowCostConfirmed)
            errors.Add(new("discount.error.below_cost_confirmation_required",
                $"This discount would result in selling {belowCostDtos.Count} batch(es) below cost. Confirm with belowCostConfirmed=true to proceed."));

        return new DiscountRulePreviewDto(
            sellable.Count,
            batchDtos.Take(SampleCap).ToList(),
            belowCostDtos.Take(SampleCap).ToList(),
            safeMaxPct,
            errors,
            infos);
    }

    private static DiscountRulePreviewBatchDto ToBatchDto(InventoryBatch batch, decimal discountedPrice) =>
        new(batch.Id, batch.Item?.Name ?? string.Empty, batch.BatchNumber, batch.SalesPrice, batch.CostPrice, discountedPrice);

    private static decimal? ComputeSafeMax(decimal salesPrice, decimal costPrice)
    {
        if (salesPrice <= 0 || costPrice <= 0 || salesPrice <= costPrice)
            return null;
        return Math.Floor((1m - costPrice / salesPrice) * 10000m) / 100m;
    }

    private static bool WindowsOverlap(
        DateTimeOffset? existStart, DateTimeOffset? existEnd,
        DateTimeOffset? newStart, DateTimeOffset? newEnd)
    {
        bool existEndAfterNewStart = !existEnd.HasValue || !newStart.HasValue || existEnd > newStart;
        bool existStartBeforeNewEnd = !existStart.HasValue || !newEnd.HasValue || existStart < newEnd;
        return existEndAfterNewStart && existStartBeforeNewEnd;
    }
}
