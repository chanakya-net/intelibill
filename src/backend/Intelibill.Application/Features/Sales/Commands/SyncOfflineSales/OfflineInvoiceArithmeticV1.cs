using System.Numerics;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

internal sealed record OfflineExpectedLine(
    decimal PreTaxAmount,
    decimal ItemDiscountAmount,
    decimal TaxableBeforeSaleDiscount,
    decimal SaleDiscountAmount,
    decimal TaxableAmount,
    decimal TaxAmount,
    decimal TotalAmount);

internal sealed record OfflineExpectedAmounts(
    IReadOnlyList<OfflineExpectedLine> Lines,
    decimal EligibleSubtotal,
    decimal SubtotalBeforeDiscount,
    IReadOnlyList<decimal> AcceptedTotalsBeforeDiscount,
    decimal TotalDiscountAmount,
    decimal TotalTaxAmount,
    decimal TotalAmount);

// Version 1: money in paise, quantity in thousandths, percentages in basis points, every division rounded
// half away from zero, sale discount allocated by capped largest remainder. A tax-inclusive line totals its
// shelf price less the tax-inclusive equivalent of its discounts (as SalePricingMath does online), so an
// inclusive price is never exceeded. Mirrors offline-sale-pricing-calculator.ts.
internal static class OfflineInvoiceArithmeticV1
{
    private const int MoneyScale = 2;
    private const int QuantityScale = 3;
    private const int PercentScale = 2;
    private static readonly decimal[] PowersOfTen = [1m, 10m, 100m, 1_000m];
    private static readonly BigInteger QuantityUnit = 1_000;
    private static readonly BigInteger PercentUnit = 10_000;

    internal static OfflineExpectedAmounts Calculate(OfflineSaleSyncCommand sale)
    {
        var count = sale.Items.Count;
        var gross = new BigInteger[count];
        var preTax = new BigInteger[count];
        var itemDiscount = new BigInteger[count];
        var beforeSale = new BigInteger[count];
        var rates = new BigInteger[count];
        var eligible = BigInteger.Zero;

        for (var i = 0; i < count; i++)
        {
            var line = sale.Items[i];
            rates[i] = Scaled(line.TaxRatePercent, PercentScale);
            var exactGross = Scaled(line.SalesPrice, MoneyScale) * Scaled(line.Quantity, QuantityScale);
            gross[i] = RoundDiv(exactGross, QuantityUnit);
            preTax[i] = IsTaxInclusive(line, rates[i])
                ? RoundDiv(exactGross * PercentUnit, QuantityUnit * (PercentUnit + rates[i]))
                : gross[i];
            itemDiscount[i] = line.IsServiceLine ? BigInteger.Zero : ItemDiscount(line, preTax[i]);
            beforeSale[i] = preTax[i] - itemDiscount[i];
            if (!line.IsServiceLine)
                eligible += beforeSale[i];
        }

        var saleDiscount = SaleDiscount(sale, eligible);
        var weights = new BigInteger[count];
        for (var i = 0; i < count; i++)
            weights[i] = sale.Items[i].IsServiceLine || beforeSale[i] <= 0 ? BigInteger.Zero : beforeSale[i];
        var allocation = AllocateLargestRemainder(saleDiscount, weights);

        var lines = new List<OfflineExpectedLine>(count);
        BigInteger subtotal = 0, discounts = 0, taxes = 0, totals = 0;
        for (var i = 0; i < count; i++)
        {
            var taxable = beforeSale[i] - allocation[i];
            BigInteger tax, total;
            if (IsTaxInclusive(sale.Items[i], rates[i]))
            {
                var grossDiscount = RoundDiv((itemDiscount[i] + allocation[i]) * (PercentUnit + rates[i]), PercentUnit);
                total = BigInteger.Max(BigInteger.Zero, gross[i] - grossDiscount);
                tax = BigInteger.Max(BigInteger.Zero, total - taxable);
            }
            else
            {
                tax = RoundDiv(taxable * rates[i], PercentUnit);
                total = taxable + tax;
            }

            subtotal += preTax[i];
            discounts += itemDiscount[i] + allocation[i];
            taxes += tax;
            totals += total;
            lines.Add(new OfflineExpectedLine(Money(preTax[i]), Money(itemDiscount[i]), Money(beforeSale[i]), Money(allocation[i]), Money(taxable), Money(tax), Money(total)));
        }

        return new OfflineExpectedAmounts(lines, Money(eligible), Money(subtotal), [Money(totals + discounts)], Money(discounts), Money(taxes), Money(totals));
    }

    internal static bool FitsScale(decimal value, int scale) => decimal.Round(value, scale) == value;

    private static bool IsTaxInclusive(OfflineSaleSyncLineCommand line, BigInteger rateBasisPoints) =>
        line.IsPriceIncludingTax && rateBasisPoints > 0;

    internal static BigInteger RoundDiv(BigInteger numerator, BigInteger denominator)
    {
        var quotient = BigInteger.DivRem(BigInteger.Abs(numerator), denominator, out var remainder);
        if (remainder * 2 >= denominator)
            quotient += 1;
        return numerator.Sign < 0 ? -quotient : quotient;
    }

    internal static BigInteger[] AllocateLargestRemainder(BigInteger total, IReadOnlyList<BigInteger> weights)
    {
        var allocation = new BigInteger[weights.Count];
        var weightSum = BigInteger.Zero;
        foreach (var weight in weights)
            weightSum += weight;
        if (total <= 0 || weightSum <= 0)
            return allocation;

        var remainders = new BigInteger[weights.Count];
        var remaining = total;
        for (var i = 0; i < weights.Count; i++)
        {
            if (weights[i] <= 0) continue;
            var share = BigInteger.DivRem(total * weights[i], weightSum, out remainders[i]);
            allocation[i] = BigInteger.Min(share, weights[i]);
            remaining -= allocation[i];
        }

        var order = Enumerable.Range(0, weights.Count)
            .Where(i => weights[i] > 0)
            .OrderByDescending(i => remainders[i])
            .ThenBy(i => i)
            .ToList();
        while (remaining > 0)
        {
            var progressed = false;
            foreach (var i in order)
            {
                if (remaining == 0) break;
                if (allocation[i] >= weights[i]) continue;
                allocation[i] += 1;
                remaining -= 1;
                progressed = true;
            }

            if (!progressed) break;
        }

        return allocation;
    }

    private static BigInteger ItemDiscount(OfflineSaleSyncLineCommand line, BigInteger preTax)
    {
        var overrideAmount = ApplyDiscount(preTax, line.ItemDiscountOverrideType, line.ItemDiscountOverrideValue);
        BigInteger amount;
        if (!line.ConfiguredBatchRuleId.HasValue)
        {
            amount = overrideAmount;
        }
        else
        {
            var configured = Percent(preTax, Scaled(line.ConfiguredBatchRulePercentage!.Value, PercentScale));
            amount = line.ItemDiscountOverrideType == InstantDiscountType.None ? configured : BigInteger.Min(configured, overrideAmount);
        }

        return Clamp(amount, preTax);
    }

    private static BigInteger SaleDiscount(OfflineSaleSyncCommand sale, BigInteger eligible)
    {
        var hasRule = sale.ConfiguredSaleRuleId.HasValue;
        var configured = hasRule ? Percent(eligible, Scaled(sale.ConfiguredSaleRulePercentage!.Value, PercentScale)) : BigInteger.Zero;
        BigInteger amount;
        if (sale.SaleDiscountOverrideType == InstantDiscountType.None)
        {
            amount = configured;
        }
        else
        {
            var overrideAmount = ApplyDiscount(eligible, sale.SaleDiscountOverrideType, sale.SaleDiscountOverrideValue);
            amount = hasRule ? BigInteger.Min(configured, overrideAmount) : overrideAmount;
        }

        return Clamp(amount, eligible);
    }

    private static BigInteger ApplyDiscount(BigInteger basis, InstantDiscountType type, decimal value)
    {
        if (basis <= 0 || value <= 0m) return BigInteger.Zero;
        return type switch
        {
            InstantDiscountType.Percentage => Percent(basis, Scaled(value, PercentScale)),
            InstantDiscountType.Flat => Scaled(value, MoneyScale),
            _ => BigInteger.Zero,
        };
    }

    private static BigInteger Percent(BigInteger basis, BigInteger basisPoints) => RoundDiv(basis * basisPoints, PercentUnit);

    private static BigInteger Clamp(BigInteger value, BigInteger max) => BigInteger.Min(BigInteger.Max(value, BigInteger.Zero), max);

    // Callers verify the scale first, so the scaled value is integral.
    private static BigInteger Scaled(decimal value, int scale) => new(value * PowersOfTen[scale]);

    private static decimal Money(BigInteger cents) => (decimal)cents / 100m;
}
