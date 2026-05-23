using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Services.Pricing;

internal static class SalePricingMath
{
    internal static decimal ExtractPreTaxAmount(decimal quantity, decimal salesPrice, decimal taxRatePercent, bool isPriceIncludingTax)
    {
        var gross = quantity * salesPrice;
        if (taxRatePercent <= 0m)
            return gross;

        return isPriceIncludingTax
            ? gross * 100m / (100m + taxRatePercent)
            : gross;
    }

    internal static decimal CalculateTaxAmount(decimal taxableAmount, decimal taxRatePercent)
    {
        if (taxRatePercent <= 0m)
            return 0m;
        return RoundMoney(taxableAmount * taxRatePercent / 100m);
    }

    internal static decimal CalculateLineTotalAmount(
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

    internal static decimal CalculateRevenueForCostCheck(
        decimal taxableAmount,
        decimal taxRatePercent,
        bool isPriceIncludingTax)
    {
        if (!isPriceIncludingTax)
            return taxableAmount;

        return RoundMoney(taxableAmount + CalculateTaxAmount(taxableAmount, taxRatePercent));
    }

    internal static decimal CalculateDiscountCapacity(
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

    internal static decimal CalculateLineDiscountAmount(InstantDiscount discount, decimal preTaxAmount)
    {
        return discount.Type switch
        {
            InstantDiscountType.None => 0m,
            InstantDiscountType.Percentage => CalculatePercentDiscount(discount.Value, preTaxAmount),
            InstantDiscountType.Flat => RoundMoney(discount.Value),
            _ => 0m,
        };
    }

    internal static decimal CalculateSaleDiscountAmount(InstantDiscount discount, decimal eligibleSubtotal)
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

    internal static decimal CalculateEffectiveItemDiscountAmount(
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

    internal static decimal CalculateEffectiveSaleDiscountAmount(
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

    internal static DiscountRule? SelectBestSaleLevelRule(
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

    internal static decimal CalculatePercentDiscount(decimal percent, decimal baseAmount)
    {
        if (percent <= 0m)
            return 0m;
        return RoundMoney(baseAmount * percent / 100m);
    }

    internal static decimal[] AllocateSaleDiscountAmounts(
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

    internal static decimal ComputeSafeMaxPercent(decimal preTaxAmount, decimal maxDiscountAmount)
    {
        if (preTaxAmount <= 0m)
            return 0m;

        if (maxDiscountAmount >= preTaxAmount)
            return 100m;

        if (maxDiscountAmount <= 0m)
            return 0m;

        return Math.Floor(maxDiscountAmount / preTaxAmount * 10000m) / 100m;
    }

    internal static bool IsValidInstantDiscount(InstantDiscount discount)
    {
        return discount.Type switch
        {
            InstantDiscountType.None => discount.Value == 0m,
            InstantDiscountType.Flat => discount.Value >= 0m,
            InstantDiscountType.Percentage => discount.Value >= 0m && discount.Value <= 100m,
            _ => false,
        };
    }

    internal static decimal RoundMoney(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    internal static long ToCents(decimal value) =>
        (long)Math.Round(value * 100m, 0, MidpointRounding.AwayFromZero);

    internal static decimal FromCents(long cents) => cents / 100m;
}
