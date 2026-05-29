using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Services.Pricing;

internal sealed class SalePricingCalculator(IDiscountRuleRepository discountRuleRepository) : ISalePricingCalculator
{
    public async Task<ErrorOr<SalePricingCalculationResult>> CalculateAsync(
        SalePricingCalculationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Lines is null || request.Lines.Count == 0)
        {
            return Errors.Sale.ItemsRequired;
        }

        var activeRules = await discountRuleRepository.GetActiveByShopAsync(
            request.ShopId,
            request.SaleTime,
            cancellationToken);

        var batchRulesByBatchId = activeRules
            .Where(r => r.RuleType == DiscountRuleType.BatchPercentage && r.InventoryBatchId.HasValue)
            .GroupBy(r => r.InventoryBatchId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(r => r.Percentage)
                    .ThenByDescending(r => r.CreatedAt)
                    .First());

        var saleLevelRules = activeRules
            .Where(r => r.InventoryBatchId is null
                && (r.RuleType == DiscountRuleType.SalePercentage || r.RuleType == DiscountRuleType.SaleThresholdPercentage))
            .ToList();

        var lineDrafts = new List<LineDraft>(request.Lines.Count);
        for (var lineIndex = 0; lineIndex < request.Lines.Count; lineIndex++)
        {
            var line = request.Lines[lineIndex];
            DiscountRule? batchRule = null;
            if (line.LineType == SaleLineType.Goods)
            {
                batchRulesByBatchId.TryGetValue(line.InventoryBatchId, out batchRule);
            }
            var draftOrError = DraftLine(line, lineIndex, batchRule);
            if (draftOrError.IsError)
                return draftOrError.Errors;
            lineDrafts.Add(draftOrError.Value);
        }

        var eligibleDrafts = lineDrafts
            .Where(d => d.EligibleForSaleDiscount)
            .ToList();

        var saleLevelEligibleSubtotal = SalePricingMath.RoundMoney(eligibleDrafts.Sum(d => d.TaxableAfterItemDiscount));
        if (!SalePricingMath.IsValidInstantDiscount(request.SaleDiscount)) return Errors.Sale.InvalidSaleDiscount;

        var isSaleDiscountRequested = request.SaleDiscount.Type != InstantDiscountType.None && request.SaleDiscount.Value > 0m;
        if (isSaleDiscountRequested && saleLevelEligibleSubtotal <= 0m) return Errors.Sale.NoEligibleLinesForSaleDiscount;

        var infos = new List<SalePricingInfoMessage>();

        DiscountRule? bestConfiguredSaleRule = SalePricingMath.SelectBestSaleLevelRule(saleLevelRules, saleLevelEligibleSubtotal);
        if (bestConfiguredSaleRule is not null && saleLevelEligibleSubtotal <= 0m && !isSaleDiscountRequested)
        {
            infos.Add(new SalePricingInfoMessage("sale_pricing.info.no_eligible_lines_for_configured_sale_discount", "Configured sale-level discount was available, but no eligible lines remained for sale-level discounts."));
            bestConfiguredSaleRule = null;
        }

        var saleDiscountAmount = SalePricingMath.CalculateEffectiveSaleDiscountAmount(request.SaleDiscount, saleLevelEligibleSubtotal, bestConfiguredSaleRule);
        var totalCapacity = SalePricingMath.RoundMoney(eligibleDrafts.Sum(d => d.SaleDiscountCapacity));
        if (saleDiscountAmount > totalCapacity) return Errors.Sale.SaleDiscountWouldBeBelowCost;
        var saleDiscountByLineIndex = SalePricingMath.AllocateSaleDiscountAmounts(eligibleDrafts, lineDrafts.Count, saleLevelEligibleSubtotal, saleDiscountAmount);

        var finalLines = new List<SalePricingLineCalculation>(lineDrafts.Count);
        foreach (var draft in lineDrafts)
        {
            var saleDiscountForLine = saleDiscountByLineIndex[draft.LineIndex];

            if (saleDiscountForLine > draft.SaleDiscountCapacity)
            {
                return Errors.Sale.SaleDiscountWouldBeBelowCost;
            }

            var taxableAfterAllDiscounts = SalePricingMath.RoundMoney(draft.TaxableAfterItemDiscount - saleDiscountForLine);
            var revenueForCostCheck = SalePricingMath.CalculateRevenueForCostCheck(taxableAfterAllDiscounts, draft.TaxRatePercent, draft.IsPriceIncludingTax);
            if (revenueForCostCheck < draft.CostTotal) return Errors.Sale.LineWouldBeBelowCost(draft.InventoryBatchId);

            var totalPreTaxDiscount = SalePricingMath.RoundMoney(draft.ItemDiscountAmount + saleDiscountForLine);
            var totalAmount = SalePricingMath.CalculateLineTotalAmount(draft.Quantity, draft.SalesPrice, draft.TaxRatePercent, draft.IsPriceIncludingTax, taxableAfterAllDiscounts, totalPreTaxDiscount);
            var taxAmount = SalePricingMath.RoundMoney(Math.Max(0m, totalAmount - taxableAfterAllDiscounts));

            finalLines.Add(new SalePricingLineCalculation(draft.LineType, draft.InventoryBatchId, draft.ServiceId, draft.Quantity, draft.CostPrice, draft.SalesPrice, draft.TaxRatePercent, draft.IsPriceIncludingTax, draft.PreTaxBeforeDiscount, draft.ItemDiscountAmount, saleDiscountForLine, taxableAfterAllDiscounts, taxAmount, totalAmount, draft.MaxAllowedItemDiscountFlat, draft.MaxAllowedItemDiscountPercent, draft.ConfiguredBatchRuleId, draft.ConfiguredBatchRulePercentage));
        }

        var totalTaxableAmount = SalePricingMath.RoundMoney(finalLines.Sum(l => l.TaxableAmount));
        var totalTaxAmount = SalePricingMath.RoundMoney(finalLines.Sum(l => l.TaxAmount));
        var totalDiscountAmount = SalePricingMath.RoundMoney(finalLines.Sum(l => l.ItemDiscountAmount + l.SaleDiscountAmount));
        var totalAmountSum = SalePricingMath.RoundMoney(finalLines.Sum(l => l.TotalAmount));
        return new SalePricingCalculationResult(finalLines, saleLevelEligibleSubtotal, totalTaxableAmount, totalTaxAmount, totalDiscountAmount, totalAmountSum, bestConfiguredSaleRule is null ? null : new SalePricingConfiguredSaleRule(bestConfiguredSaleRule.Id, bestConfiguredSaleRule.RuleType, bestConfiguredSaleRule.Percentage, bestConfiguredSaleRule.ThresholdAmount), infos);
    }

    private static ErrorOr<LineDraft> DraftLine(
        SalePricingLineCalculationRequest line,
        int lineIndex,
        DiscountRule? configuredBatchRule)
    {
        if (line.Quantity <= 0m)
        {
            return Errors.Sale.QuantityMustBePositive(line.InventoryBatchId);
        }

        if (line.SalesPrice < 0m || line.CostPrice < 0m || line.TaxRatePercent < 0m)
        {
            return Errors.Sale.InvalidPricingInput(line.InventoryBatchId);
        }

        if (!SalePricingMath.IsValidInstantDiscount(line.ItemDiscount)) return Errors.Sale.InvalidDiscount(line.InventoryBatchId);

        var preTaxBeforeDiscount = SalePricingMath.RoundMoney(SalePricingMath.ExtractPreTaxAmount(line.Quantity, line.SalesPrice, line.TaxRatePercent, line.IsPriceIncludingTax));
        var costTotal = SalePricingMath.RoundMoney(line.CostPrice * line.Quantity);
        var maxAllowedItemDiscountFlat = SalePricingMath.CalculateDiscountCapacity(preTaxBeforeDiscount, costTotal, line.TaxRatePercent, line.IsPriceIncludingTax);
        var maxAllowedItemDiscountPercent = SalePricingMath.ComputeSafeMaxPercent(preTaxBeforeDiscount, maxAllowedItemDiscountFlat);
        var itemDiscountAmount = SalePricingMath.CalculateEffectiveItemDiscountAmount(line.ItemDiscount, preTaxBeforeDiscount, configuredBatchRule);
        if (itemDiscountAmount > maxAllowedItemDiscountFlat) return Errors.Sale.ItemDiscountWouldBeBelowCost(line.InventoryBatchId);

        var taxableAfterItemDiscount = SalePricingMath.RoundMoney(preTaxBeforeDiscount - itemDiscountAmount);
        var revenueAfterItemDiscount = SalePricingMath.CalculateRevenueForCostCheck(taxableAfterItemDiscount, line.TaxRatePercent, line.IsPriceIncludingTax);
        if (revenueAfterItemDiscount < costTotal) return Errors.Sale.LineWouldBeBelowCost(line.InventoryBatchId);
        var saleDiscountCapacity = SalePricingMath.CalculateDiscountCapacity(taxableAfterItemDiscount, costTotal, line.TaxRatePercent, line.IsPriceIncludingTax);
        return new LineDraft(lineIndex, line.LineType, line.InventoryBatchId, line.ServiceId, line.Quantity, line.CostPrice, line.SalesPrice, line.TaxRatePercent, line.IsPriceIncludingTax, preTaxBeforeDiscount, costTotal, itemDiscountAmount, taxableAfterItemDiscount, saleDiscountCapacity, maxAllowedItemDiscountFlat, maxAllowedItemDiscountPercent, configuredBatchRule?.Id, configuredBatchRule?.Percentage, line.IsSaleDiscountEligible);
    }
}
