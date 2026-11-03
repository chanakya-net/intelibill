using System.Globalization;
using System.Numerics;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

// Unversioned payloads came from the browser calculator that ran on IEEE doubles and rounded with
// Number(x.toFixed(2)). .NET doubles use the same IEEE operations, so this replays that chain step for
// step; toFixed is round half away from zero on the exact binary value, reproduced with BigInteger.
internal static class OfflineInvoiceArithmeticLegacy
{
    internal static OfflineExpectedAmounts Calculate(OfflineSaleSyncCommand sale)
    {
        var count = sale.Items.Count;
        var preTax = new double[count];
        var itemDiscount = new double[count];
        var beforeSale = new double[count];
        var taxBeforeSale = new double[count];
        var totalBeforeSale = new double[count];
        var eligibleSum = 0d;
        var goodsCount = 0;

        for (var i = 0; i < count; i++)
        {
            var line = sale.Items[i];
            var quantity = Math.Max(0d, Js(line.Quantity));
            var rate = Js(line.TaxRatePercent);
            var gross = RoundMoney(Js(line.SalesPrice) * quantity);
            preTax[i] = line.IsPriceIncludingTax ? RoundMoney(gross / (1 + rate / 100)) : gross;
            itemDiscount[i] = line.IsServiceLine ? 0d : Clamp(ItemDiscount(line, preTax[i]), 0d, preTax[i]);
            beforeSale[i] = RoundMoney(preTax[i] - itemDiscount[i]);
            taxBeforeSale[i] = RoundMoney(beforeSale[i] * (rate / 100));
            totalBeforeSale[i] = RoundMoney(beforeSale[i] + taxBeforeSale[i]);
            if (line.IsServiceLine) continue;
            eligibleSum += beforeSale[i];
            goodsCount++;
        }

        var eligible = RoundMoney(eligibleSum);
        var saleDiscountTotal = Clamp(SaleDiscount(sale, eligible), 0d, eligible);

        var lines = new List<OfflineExpectedLine>(count);
        double allocated = 0d, preTaxSum = 0d, itemSum = 0d, saleSum = 0d, taxSum = 0d, totalSum = 0d;
        var goodsAllocated = 0;
        for (var i = 0; i < count; i++)
        {
            var line = sale.Items[i];
            double amount, taxable, tax, total;
            if (line.IsServiceLine)
            {
                amount = 0d;
                taxable = beforeSale[i];
                tax = taxBeforeSale[i];
                total = totalBeforeSale[i];
            }
            else
            {
                var weight = eligible > 0 ? beforeSale[i] / eligible : 0d;
                goodsAllocated++;
                amount = goodsAllocated == goodsCount
                    ? RoundMoney(saleDiscountTotal - allocated)
                    : RoundMoney(saleDiscountTotal * weight);
                allocated = RoundMoney(allocated + amount);
                taxable = RoundMoney(beforeSale[i] - amount);
                tax = RoundMoney(taxable * (Js(line.TaxRatePercent) / 100));
                total = RoundMoney(taxable + tax);
            }

            preTaxSum += preTax[i];
            itemSum += itemDiscount[i];
            saleSum += amount;
            taxSum += tax;
            totalSum += total;
            lines.Add(new OfflineExpectedLine(ToMoney(preTax[i]), ToMoney(itemDiscount[i]), ToMoney(beforeSale[i]), ToMoney(amount), ToMoney(taxable), ToMoney(tax), ToMoney(total)));
        }

        var subtotal = ToMoney(RoundMoney(preTaxSum));
        var totalDiscount = ToMoney(RoundMoney(RoundMoney(itemSum) + RoundMoney(saleSum)));
        // The browser sent the pre-tax subtotal as totalBeforeDiscount; the domain defines it as line totals plus discounts.
        var domainTotalBeforeDiscount = lines.Sum(l => l.TotalAmount + l.ItemDiscountAmount + l.SaleDiscountAmount);
        return new OfflineExpectedAmounts(
            lines,
            ToMoney(eligible),
            subtotal,
            [domainTotalBeforeDiscount, subtotal],
            totalDiscount,
            ToMoney(RoundMoney(taxSum)),
            ToMoney(RoundMoney(totalSum)));
    }

    private static double ItemDiscount(OfflineSaleSyncLineCommand line, double preTax)
    {
        if (!line.ConfiguredBatchRuleId.HasValue)
            return ApplyDiscount(preTax, line.ItemDiscountOverrideType, line.ItemDiscountOverrideValue);

        var configured = RoundMoney(preTax * (Js(line.ConfiguredBatchRulePercentage!.Value) / 100));
        if (line.ItemDiscountOverrideType == InstantDiscountType.None) return configured;

        var overrideAmount = ApplyDiscount(preTax, line.ItemDiscountOverrideType, line.ItemDiscountOverrideValue);
        return RoundMoney(Math.Min(configured, overrideAmount));
    }

    private static double SaleDiscount(OfflineSaleSyncCommand sale, double eligible)
    {
        var hasRule = sale.ConfiguredSaleRuleId.HasValue;
        var configured = hasRule ? RoundMoney(eligible * (Js(sale.ConfiguredSaleRulePercentage!.Value) / 100)) : 0d;
        if (sale.SaleDiscountOverrideType == InstantDiscountType.None) return configured;

        var overrideAmount = ApplyDiscount(eligible, sale.SaleDiscountOverrideType, sale.SaleDiscountOverrideValue);
        return hasRule ? RoundMoney(Math.Min(configured, overrideAmount)) : overrideAmount;
    }

    private static double ApplyDiscount(double basis, InstantDiscountType type, decimal value)
    {
        var jsValue = Js(value);
        if (basis <= 0 || jsValue <= 0) return 0d;
        return type switch
        {
            InstantDiscountType.Percentage => RoundMoney(basis * (jsValue / 100)),
            InstantDiscountType.Flat => RoundMoney(jsValue),
            _ => 0d,
        };
    }

    private static double Clamp(double value, double min, double max) => Math.Min(Math.Max(value, min), max);

    // The browser held each input as the double nearest its JSON text, which round-trips through decimal.
    private static double Js(decimal value) => double.Parse(value.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    // Number(value.toFixed(2)): the parsed string is the double nearest cents / 100, which IEEE division also yields.
    private static double RoundMoney(double value) => (double)ToFixedCents(value) / 100d;

    private static decimal ToMoney(double roundedValue) => (decimal)ToFixedCents(roundedValue) / 100m;

    internal static BigInteger ToFixedCents(double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        var exponent = (int)((bits >> 52) & 0x7FF);
        var fraction = bits & 0xF_FFFF_FFFF_FFFFL;
        if (exponent == 0 && fraction == 0) return BigInteger.Zero;

        BigInteger mantissa = exponent == 0 ? fraction : fraction | (1L << 52);
        var power = exponent == 0 ? -1074 : exponent - 1075;
        var scaled = mantissa * 100;
        BigInteger cents;
        if (power >= 0)
        {
            cents = scaled << power;
        }
        else
        {
            var denominator = BigInteger.One << -power;
            cents = BigInteger.DivRem(scaled, denominator, out var remainder);
            if (remainder * 2 >= denominator)
                cents += 1;
        }

        return bits < 0 ? -cents : cents;
    }
}
