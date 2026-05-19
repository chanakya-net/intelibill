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
            batchRulesByBatchId.TryGetValue(request.Lines[lineIndex].InventoryBatchId, out var batchRule);
            var draftOrError = DraftLine(request.Lines[lineIndex], lineIndex, batchRule);
            if (draftOrError.IsError)
                return draftOrError.Errors;
            lineDrafts.Add(draftOrError.Value);
        }

        var eligibleDrafts = lineDrafts
            .Where(d => d.IsSaleDiscountEligible)
            .ToList();

        var saleLevelEligibleSubtotal = RoundMoney(eligibleDrafts.Sum(d => d.TaxableAfterItemDiscount));

        if (!IsValidInstantDiscount(request.SaleDiscount))
        {
            return Errors.Sale.InvalidSaleDiscount;
        }

        var isSaleDiscountRequested = request.SaleDiscount.Type != InstantDiscountType.None && request.SaleDiscount.Value > 0m;
        if (isSaleDiscountRequested && saleLevelEligibleSubtotal <= 0m)
        {
            return Errors.Sale.NoEligibleLinesForSaleDiscount;
        }

        var infos = new List<SalePricingInfoMessage>();

        DiscountRule? bestConfiguredSaleRule = SelectBestSaleLevelRule(saleLevelRules, saleLevelEligibleSubtotal);
        if (bestConfiguredSaleRule is not null && saleLevelEligibleSubtotal <= 0m && !isSaleDiscountRequested)
        {
            infos.Add(new SalePricingInfoMessage(
                "sale_pricing.info.no_eligible_lines_for_configured_sale_discount",
                "Configured sale-level discount was available, but no eligible lines remained for sale-level discounts."));
            bestConfiguredSaleRule = null;
        }

        var saleDiscountAmount = CalculateEffectiveSaleDiscountAmount(
            request.SaleDiscount,
            saleLevelEligibleSubtotal,
            bestConfiguredSaleRule);

        var totalCapacity = RoundMoney(eligibleDrafts.Sum(d => d.SaleDiscountCapacity));
        if (saleDiscountAmount > totalCapacity)
        {
            return Errors.Sale.SaleDiscountWouldBeBelowCost;
        }

        var saleDiscountByLineIndex = AllocateSaleDiscountAmounts(
            eligibleDrafts,
            lineDrafts.Count,
            saleLevelEligibleSubtotal,
            saleDiscountAmount);

        var finalLines = new List<SalePricingLineCalculation>(lineDrafts.Count);
        foreach (var draft in lineDrafts)
        {
            var saleDiscountForLine = saleDiscountByLineIndex[draft.LineIndex];

            if (saleDiscountForLine > draft.SaleDiscountCapacity)
            {
                return Errors.Sale.SaleDiscountWouldBeBelowCost;
            }

            var taxableAfterAllDiscounts = RoundMoney(draft.TaxableAfterItemDiscount - saleDiscountForLine);
            var revenueForCostCheck = CalculateRevenueForCostCheck(
                taxableAfterAllDiscounts,
                draft.TaxRatePercent,
                draft.IsPriceIncludingTax);
            if (revenueForCostCheck < draft.CostTotal)
            {
                return Errors.Sale.LineWouldBeBelowCost(draft.InventoryBatchId);
            }

            var totalPreTaxDiscount = RoundMoney(draft.ItemDiscountAmount + saleDiscountForLine);
            var totalAmount = CalculateLineTotalAmount(
                draft.Quantity,
                draft.SalesPrice,
                draft.TaxRatePercent,
                draft.IsPriceIncludingTax,
                taxableAfterAllDiscounts,
                totalPreTaxDiscount);
            var taxAmount = RoundMoney(Math.Max(0m, totalAmount - taxableAfterAllDiscounts));

            finalLines.Add(new SalePricingLineCalculation(
                draft.InventoryBatchId,
                draft.Quantity,
                draft.CostPrice,
                draft.SalesPrice,
                draft.TaxRatePercent,
                draft.IsPriceIncludingTax,
                draft.PreTaxBeforeDiscount,
                draft.ItemDiscountAmount,
                saleDiscountForLine,
                taxableAfterAllDiscounts,
                taxAmount,
                totalAmount,
                draft.MaxAllowedItemDiscountFlat,
                draft.MaxAllowedItemDiscountPercent,
                draft.ConfiguredBatchRuleId,
                draft.ConfiguredBatchRulePercentage));
        }

        var totalTaxableAmount = RoundMoney(finalLines.Sum(l => l.TaxableAmount));
        var totalTaxAmount = RoundMoney(finalLines.Sum(l => l.TaxAmount));
        var totalDiscountAmount = RoundMoney(finalLines.Sum(l => l.ItemDiscountAmount + l.SaleDiscountAmount));
        var totalAmountSum = RoundMoney(finalLines.Sum(l => l.TotalAmount));

        return new SalePricingCalculationResult(
            finalLines,
            saleLevelEligibleSubtotal,
            totalTaxableAmount,
            totalTaxAmount,
            totalDiscountAmount,
            totalAmountSum,
            bestConfiguredSaleRule is null
                ? null
                : new SalePricingConfiguredSaleRule(
                    bestConfiguredSaleRule.Id,
                    bestConfiguredSaleRule.RuleType,
                    bestConfiguredSaleRule.Percentage,
                    bestConfiguredSaleRule.ThresholdAmount),
            infos);
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

        if (!IsValidInstantDiscount(line.ItemDiscount))
        {
            return Errors.Sale.InvalidDiscount(line.InventoryBatchId);
        }

        var preTaxBeforeDiscount = RoundMoney(ExtractPreTaxAmount(line.Quantity, line.SalesPrice, line.TaxRatePercent, line.IsPriceIncludingTax));
        var costTotal = RoundMoney(line.CostPrice * line.Quantity);

        var maxAllowedItemDiscountFlat = CalculateDiscountCapacity(
            preTaxBeforeDiscount,
            costTotal,
            line.TaxRatePercent,
            line.IsPriceIncludingTax);
        var maxAllowedItemDiscountPercent = ComputeSafeMaxPercent(preTaxBeforeDiscount, maxAllowedItemDiscountFlat);

        var itemDiscountAmount = CalculateEffectiveItemDiscountAmount(
            line.ItemDiscount,
            preTaxBeforeDiscount,
            configuredBatchRule);

        if (itemDiscountAmount > maxAllowedItemDiscountFlat)
        {
            return Errors.Sale.ItemDiscountWouldBeBelowCost(line.InventoryBatchId);
        }

        var taxableAfterItemDiscount = RoundMoney(preTaxBeforeDiscount - itemDiscountAmount);
        var revenueAfterItemDiscount = CalculateRevenueForCostCheck(
            taxableAfterItemDiscount,
            line.TaxRatePercent,
            line.IsPriceIncludingTax);
        if (revenueAfterItemDiscount < costTotal)
        {
            return Errors.Sale.LineWouldBeBelowCost(line.InventoryBatchId);
        }

        var saleDiscountCapacity = CalculateDiscountCapacity(
            taxableAfterItemDiscount,
            costTotal,
            line.TaxRatePercent,
            line.IsPriceIncludingTax);

        return new LineDraft(
            lineIndex,
            line.InventoryBatchId,
            line.Quantity,
            line.CostPrice,
            line.SalesPrice,
            line.TaxRatePercent,
            line.IsPriceIncludingTax,
            preTaxBeforeDiscount,
            costTotal,
            itemDiscountAmount,
            taxableAfterItemDiscount,
            saleDiscountCapacity,
            maxAllowedItemDiscountFlat,
            maxAllowedItemDiscountPercent,
            configuredBatchRule?.Id,
            configuredBatchRule?.Percentage);
    }

    private static decimal ExtractPreTaxAmount(decimal quantity, decimal salesPrice, decimal taxRatePercent, bool isPriceIncludingTax)
    {
        var gross = quantity * salesPrice;
        if (taxRatePercent <= 0m)
            return gross;

        return isPriceIncludingTax
            ? gross * 100m / (100m + taxRatePercent)
            : gross;
    }

    private static decimal CalculateTaxAmount(decimal taxableAmount, decimal taxRatePercent)
    {
        if (taxRatePercent <= 0m)
            return 0m;
        return RoundMoney(taxableAmount * taxRatePercent / 100m);
    }

    private static decimal CalculateLineTotalAmount(
        decimal quantity,
        decimal salesPrice,
        decimal taxRatePercent,
        bool isPriceIncludingTax,
        decimal taxableAmount,
        decimal totalPreTaxDiscount)
    {
        if (!isPriceIncludingTax)
            return RoundMoney(taxableAmount + CalculateTaxAmount(taxableAmount, taxRatePercent));

        if (taxRatePercent <= 0m)
            return taxableAmount;

        var grossBeforeDiscount = RoundMoney(quantity * salesPrice);
        var grossDiscount = RoundMoney(totalPreTaxDiscount * (100m + taxRatePercent) / 100m);
        return RoundMoney(Math.Max(0m, grossBeforeDiscount - grossDiscount));
    }

    private static decimal CalculateRevenueForCostCheck(
        decimal taxableAmount,
        decimal taxRatePercent,
        bool isPriceIncludingTax)
    {
        if (!isPriceIncludingTax)
            return taxableAmount;

        return RoundMoney(taxableAmount + CalculateTaxAmount(taxableAmount, taxRatePercent));
    }

    private static decimal CalculateDiscountCapacity(
        decimal taxableAmount,
        decimal costTotal,
        decimal taxRatePercent,
        bool isPriceIncludingTax)
    {
        if (!isPriceIncludingTax || taxRatePercent <= 0m)
            return RoundMoney(Math.Max(0m, taxableAmount - costTotal));

        var grossAmount = CalculateRevenueForCostCheck(taxableAmount, taxRatePercent, isPriceIncludingTax);
        var grossCapacity = RoundMoney(Math.Max(0m, grossAmount - costTotal));
        return RoundMoney(grossCapacity * 100m / (100m + taxRatePercent));
    }

    private static decimal CalculateLineDiscountAmount(InstantDiscount discount, decimal preTaxAmount)
    {
        return discount.Type switch
        {
            InstantDiscountType.None => 0m,
            InstantDiscountType.Percentage => CalculatePercentDiscount(discount.Value, preTaxAmount),
            InstantDiscountType.Flat => RoundMoney(discount.Value),
            _ => 0m,
        };
    }

    private static decimal CalculateSaleDiscountAmount(InstantDiscount discount, decimal eligibleSubtotal)
    {
        if (eligibleSubtotal <= 0m)
            return 0m;

        return discount.Type switch
        {
            InstantDiscountType.None => 0m,
            InstantDiscountType.Percentage => CalculatePercentDiscount(discount.Value, eligibleSubtotal),
            InstantDiscountType.Flat => RoundMoney(discount.Value),
            _ => 0m,
        };
    }

    private static decimal CalculateEffectiveItemDiscountAmount(
        InstantDiscount discount,
        decimal preTaxAmount,
        DiscountRule? configuredBatchRule)
    {
        if (configuredBatchRule is null)
        {
            return CalculateLineDiscountAmount(discount, preTaxAmount);
        }

        var configuredAmount = CalculatePercentDiscount(configuredBatchRule.Percentage, preTaxAmount);

        if (discount.Type == InstantDiscountType.None)
        {
            return configuredAmount;
        }

        var overrideAmount = CalculateLineDiscountAmount(discount, preTaxAmount);
        return RoundMoney(Math.Min(configuredAmount, overrideAmount));
    }

    private static decimal CalculateEffectiveSaleDiscountAmount(
        InstantDiscount saleDiscount,
        decimal eligibleSubtotal,
        DiscountRule? configuredSaleRule)
    {
        var configuredAmount = configuredSaleRule is null
            ? 0m
            : CalculatePercentDiscount(configuredSaleRule.Percentage, eligibleSubtotal);

        if (saleDiscount.Type == InstantDiscountType.None)
        {
            return configuredAmount;
        }

        var overrideAmount = CalculateSaleDiscountAmount(saleDiscount, eligibleSubtotal);
        return configuredSaleRule is null
            ? overrideAmount
            : RoundMoney(Math.Min(configuredAmount, overrideAmount));
    }

    private static DiscountRule? SelectBestSaleLevelRule(
        List<DiscountRule> saleRules,
        decimal eligibleSubtotal)
    {
        if (saleRules.Count == 0)
            return null;

        var eligibleRules = saleRules
            .Where(r => r.RuleType == DiscountRuleType.SalePercentage
                || (r.RuleType == DiscountRuleType.SaleThresholdPercentage
                    && r.ThresholdAmount.HasValue
                    && eligibleSubtotal >= r.ThresholdAmount.Value))
            .ToList();

        if (eligibleRules.Count == 0)
            return null;

        return eligibleRules
            .OrderByDescending(r => CalculatePercentDiscount(r.Percentage, eligibleSubtotal))
            .ThenByDescending(r => r.RuleType == DiscountRuleType.SaleThresholdPercentage)
            .ThenByDescending(r => r.ThresholdAmount ?? 0m)
            .ThenByDescending(r => r.CreatedAt)
            .First();
    }

    private static decimal CalculatePercentDiscount(decimal percent, decimal baseAmount)
    {
        if (percent <= 0m)
            return 0m;
        return RoundMoney(baseAmount * percent / 100m);
    }

    private static decimal[] AllocateSaleDiscountAmounts(
        IReadOnlyList<LineDraft> eligibleDrafts,
        int totalLines,
        decimal eligibleSubtotal,
        decimal saleDiscountAmount)
    {
        var result = new decimal[totalLines];
        if (saleDiscountAmount <= 0m || eligibleDrafts.Count == 0 || eligibleSubtotal <= 0m || totalLines <= 0)
            return result;

        var totalCents = ToCents(saleDiscountAmount);
        var subtotalCents = ToCents(eligibleSubtotal);
        if (totalCents <= 0 || subtotalCents <= 0)
            return result;

        var allocations = new long[eligibleDrafts.Count];
        var remainders = new decimal[eligibleDrafts.Count];
        var capacities = eligibleDrafts.Select(d => ToCents(d.SaleDiscountCapacity)).ToArray();

        long allocatedCents = 0;
        for (var i = 0; i < eligibleDrafts.Count; i++)
        {
            var draft = eligibleDrafts[i];
            var weightCents = ToCents(draft.TaxableAfterItemDiscount);
            if (weightCents <= 0)
            {
                allocations[i] = 0;
                remainders[i] = 0m;
                continue;
            }

            var exactShare = (decimal)totalCents * weightCents / subtotalCents;
            var floorShare = (long)Math.Floor(exactShare);
            var capped = Math.Min(floorShare, capacities[i]);
            allocations[i] = capped;
            allocatedCents += capped;
            remainders[i] = exactShare - floorShare;
        }

        var remaining = totalCents - allocatedCents;
        if (remaining > 0)
        {
            var candidates = Enumerable.Range(0, eligibleDrafts.Count)
                .Where(i => allocations[i] < capacities[i])
                .OrderByDescending(i => remainders[i])
                .ToList();

            var idx = 0;
            while (remaining > 0 && candidates.Count > 0)
            {
                var i = candidates[idx];
                if (allocations[i] < capacities[i])
                {
                    allocations[i] += 1;
                    remaining -= 1;
                }

                idx++;
                if (idx >= candidates.Count) idx = 0;

                if (idx == 0 && candidates.All(j => allocations[j] >= capacities[j]))
                    break;
            }
        }

        for (var i = 0; i < eligibleDrafts.Count; i++)
        {
            if (allocations[i] <= 0) continue;
            result[eligibleDrafts[i].LineIndex] = FromCents(allocations[i]);
        }

        return result;
    }

    private static decimal ComputeSafeMaxPercent(decimal preTaxAmount, decimal maxDiscountAmount)
    {
        if (preTaxAmount <= 0m)
            return 0m;

        if (maxDiscountAmount >= preTaxAmount)
            return 100m;

        if (maxDiscountAmount <= 0m)
            return 0m;

        return Math.Floor(maxDiscountAmount / preTaxAmount * 10000m) / 100m;
    }

    private static bool IsValidInstantDiscount(InstantDiscount discount)
    {
        return discount.Type switch
        {
            InstantDiscountType.None => discount.Value == 0m,
            InstantDiscountType.Flat => discount.Value >= 0m,
            InstantDiscountType.Percentage => discount.Value >= 0m && discount.Value <= 100m,
            _ => false,
        };
    }

    private static decimal RoundMoney(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static long ToCents(decimal value) =>
        (long)Math.Round(value * 100m, 0, MidpointRounding.AwayFromZero);

    private static decimal FromCents(long cents) => cents / 100m;

    private sealed record LineDraft(
        int LineIndex,
        Guid InventoryBatchId,
        decimal Quantity,
        decimal CostPrice,
        decimal SalesPrice,
        decimal TaxRatePercent,
        bool IsPriceIncludingTax,
        decimal PreTaxBeforeDiscount,
        decimal CostTotal,
        decimal ItemDiscountAmount,
        decimal TaxableAfterItemDiscount,
        decimal SaleDiscountCapacity,
        decimal MaxAllowedItemDiscountFlat,
        decimal MaxAllowedItemDiscountPercent,
        Guid? ConfiguredBatchRuleId,
        decimal? ConfiguredBatchRulePercentage)
    {
        public bool IsSaleDiscountEligible => SaleDiscountCapacity > 0m && TaxableAfterItemDiscount > 0m;
    }
}
