using Intelibill.Application.Features.Discounts.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Discounts.Services;

public sealed class DiscountRuleValidationService(
    IInventoryBatchRepository inventoryBatchRepository,
    IDiscountRuleRepository discountRuleRepository)
{
    private const int SampleCap = 5;

    public async Task<DiscountRulePreviewDto> PreviewAsync(
        Guid shopId,
        DiscountRuleType ruleType,
        decimal percentage,
        decimal? thresholdAmount,
        Guid? inventoryBatchId,
        DateTimeOffset? startsAt,
        DateTimeOffset? endsAt,
        bool belowCostConfirmed,
        Guid? excludeRuleId,
        CancellationToken cancellationToken)
    {
        var errors = new List<DiscountRulePreviewMessageDto>();
        var infos = new List<DiscountRulePreviewMessageDto>();

        if (percentage <= 0 || percentage > 100)
        {
            errors.Add(new("discount.error.percent_out_of_range", "Percentage must be greater than 0 and at most 100."));
            return new DiscountRulePreviewDto(0, [], [], null, errors, infos);
        }

        if (startsAt.HasValue && endsAt.HasValue && endsAt <= startsAt)
        {
            errors.Add(new("discount.error.window_invalid", "EndsAt must be after StartsAt."));
            return new DiscountRulePreviewDto(0, [], [], null, errors, infos);
        }

        return ruleType switch
        {
            DiscountRuleType.BatchPercentage =>
                await PreviewBatchRuleAsync(shopId, percentage, inventoryBatchId, startsAt, endsAt, belowCostConfirmed, excludeRuleId, errors, infos, cancellationToken),
            DiscountRuleType.SalePercentage or DiscountRuleType.SaleThresholdPercentage =>
                await PreviewSaleLevelRuleAsync(shopId, ruleType, percentage, thresholdAmount, startsAt, endsAt, belowCostConfirmed, excludeRuleId, errors, infos, cancellationToken),
            _ => new DiscountRulePreviewDto(0, [], [], null,
                [new("discount.error.unsupported_rule_type", $"Discount rule type '{ruleType}' is not supported.")], infos)
        };
    }

    private async Task<DiscountRulePreviewDto> PreviewBatchRuleAsync(
        Guid shopId,
        decimal percentage,
        Guid? inventoryBatchId,
        DateTimeOffset? startsAt,
        DateTimeOffset? endsAt,
        bool belowCostConfirmed,
        Guid? excludeRuleId,
        List<DiscountRulePreviewMessageDto> errors,
        List<DiscountRulePreviewMessageDto> infos,
        CancellationToken cancellationToken)
    {
        if (!inventoryBatchId.HasValue)
        {
            errors.Add(new("discount.error.batch_required", "Batch ID is required for batch percentage rules."));
            return new DiscountRulePreviewDto(0, [], [], null, errors, infos);
        }

        var batch = await inventoryBatchRepository.GetByIdWithItemAsync(
            inventoryBatchId.Value, shopId, cancellationToken);

        if (batch is null)
        {
            errors.Add(new("discount.error.batch_not_found", "Inventory batch was not found."));
            return new DiscountRulePreviewDto(0, [], [], null, errors, infos);
        }

        if (batch.IsVoided)
            errors.Add(new("discount.error.batch_voided", "Cannot apply a discount rule to a voided batch."));

        var existingRules = await discountRuleRepository.GetAllActiveByBatchAsync(
            shopId, inventoryBatchId.Value, cancellationToken);

        var overlapping = existingRules
            .Where(r => !excludeRuleId.HasValue || r.Id != excludeRuleId.Value)
            .Where(r => WindowsOverlap(r.StartsAt, r.EndsAt, startsAt, endsAt))
            .ToList();

        if (overlapping.Count > 0)
            errors.Add(new("discount.error.overlap",
                $"This batch already has {overlapping.Count} active discount rule(s) with an overlapping time window."));

        var discountedPrice = batch.SalesPrice * (1m - percentage / 100m);
        var isBelowCost = discountedPrice < batch.CostPrice;
        var safeMaxPct = ComputeSafeMax(batch.SalesPrice, batch.CostPrice);
        var batchDto = ToBatchDto(batch, discountedPrice);

        if (isBelowCost && !belowCostConfirmed)
            errors.Add(new("discount.error.below_cost_confirmation_required",
                "This discount would result in selling below cost. Confirm with belowCostConfirmed=true to proceed."));

        var affectedCount = batch.IsVoided ? 0 : 1;
        IReadOnlyList<DiscountRulePreviewBatchDto> affectedSample = batch.IsVoided ? [] : [batchDto];
        IReadOnlyList<DiscountRulePreviewBatchDto> belowCostSample = isBelowCost && !batch.IsVoided ? [batchDto] : [];

        return new DiscountRulePreviewDto(affectedCount, affectedSample, belowCostSample, safeMaxPct, errors, infos);
    }

    private async Task<DiscountRulePreviewDto> PreviewSaleLevelRuleAsync(
        Guid shopId,
        DiscountRuleType ruleType,
        decimal percentage,
        decimal? thresholdAmount,
        DateTimeOffset? startsAt,
        DateTimeOffset? endsAt,
        bool belowCostConfirmed,
        Guid? excludeRuleId,
        List<DiscountRulePreviewMessageDto> errors,
        List<DiscountRulePreviewMessageDto> infos,
        CancellationToken cancellationToken)
    {
        if (ruleType == DiscountRuleType.SaleThresholdPercentage)
        {
            if (!thresholdAmount.HasValue || thresholdAmount.Value <= 0)
            {
                errors.Add(new("discount.error.threshold_required",
                    "ThresholdAmount is required and must be positive for threshold-based rules."));
                return new DiscountRulePreviewDto(0, [], [], null, errors, infos);
            }
        }

        var allBatches = await inventoryBatchRepository.GetByShopAsync(shopId, cancellationToken);
        var sellable = allBatches.Where(b => !b.IsVoided && b.Quantity > 0).ToList();

        if (sellable.Count == 0)
        {
            infos.Add(new("discount.info.no_affected_batches", "No active sellable batches found in this shop."));
            return new DiscountRulePreviewDto(0, [], [], null, errors, infos);
        }

        await AddSaleRuleOverlapInfoAsync(shopId, startsAt, endsAt, excludeRuleId, infos, cancellationToken);

        var batchDtos = sellable
            .Select(b => ToBatchDto(b, b.SalesPrice * (1m - percentage / 100m)))
            .ToList();

        var belowCostDtos = batchDtos.Where(b => b.DiscountedPrice < b.CostPrice).ToList();

        decimal? safeMaxPct = sellable.Any(b => b.SalesPrice > b.CostPrice)
            ? sellable
                .Where(b => b.SalesPrice > b.CostPrice)
                .Select(b => ComputeSafeMax(b.SalesPrice, b.CostPrice))
                .Min()
            : null;

        if (belowCostDtos.Count > 0 && !belowCostConfirmed)
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

    private async Task AddSaleRuleOverlapInfoAsync(
        Guid shopId,
        DateTimeOffset? startsAt,
        DateTimeOffset? endsAt,
        Guid? excludeRuleId,
        List<DiscountRulePreviewMessageDto> infos,
        CancellationToken cancellationToken)
    {
        var existing = await discountRuleRepository.GetByShopAsync(shopId, cancellationToken);
        var saleLevel = existing
            .Where(r => r.IsActive
                && (!excludeRuleId.HasValue || r.Id != excludeRuleId.Value)
                && r.InventoryBatchId is null
                && (r.RuleType == DiscountRuleType.SalePercentage || r.RuleType == DiscountRuleType.SaleThresholdPercentage)
                && WindowsOverlap(r.StartsAt, r.EndsAt, startsAt, endsAt))
            .ToList();

        if (saleLevel.Count > 0)
        {
            infos.Add(new("discount.info.possible_overlap_sale_rules",
                $"This shop already has {saleLevel.Count} active sale-level discount rule(s) with an overlapping time window."));
        }
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
