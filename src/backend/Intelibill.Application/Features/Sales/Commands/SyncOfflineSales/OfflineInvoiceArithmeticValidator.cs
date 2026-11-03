using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Codes = Intelibill.Application.Features.Sales.Commands.SyncOfflineSales.OfflineArithmeticMismatchCodes;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

// Recomputes an offline invoice from its own frozen inputs under the contract it declares and compares every
// line and header amount exactly. Current catalog prices and rule activity are not consulted here.
internal static class OfflineInvoiceArithmeticValidator
{
    internal const int CurrentVersion = 1;

    // Far beyond any retail invoice. With each line's gross (price x quantity) held to MaxMoneyInput too, every
    // amount fits numeric(18,2) and stays where the legacy double replay still resolves single paise; v1 integer
    // arithmetic is exact at any size.
    private const decimal MaxMoneyInput = 100_000_000_000m;
    private const decimal MaxQuantity = 10_000_000m;

    internal static OfflineArithmeticVerdict Validate(OfflineSaleSyncCommand sale)
    {
        if (sale.CalculationVersion is { } version && version != CurrentVersion)
        {
            return new OfflineArithmeticVerdict(
                OfflineArithmeticVerdict.UnsupportedContract,
                [new OfflineArithmeticMismatch("calculationVersion", version, CurrentVersion, Codes.UnsupportedVersion)]);
        }

        var isVersion1 = sale.CalculationVersion == CurrentVersion;
        var contract = isVersion1 ? OfflineArithmeticVerdict.Version1Contract : OfflineArithmeticVerdict.LegacyContract;
        var mismatches = new List<OfflineArithmeticMismatch>();
        CheckInputs(sale, isVersion1, mismatches);
        if (mismatches.Count > 0)
            return new OfflineArithmeticVerdict(contract, mismatches);

        var expected = isVersion1
            ? OfflineInvoiceArithmeticV1.Calculate(sale)
            : OfflineInvoiceArithmeticLegacy.Calculate(sale);

        if (sale.ConfiguredSaleRuleId.HasValue
            && sale.ConfiguredSaleRuleType == DiscountRuleType.SaleThresholdPercentage
            && expected.EligibleSubtotal < sale.ConfiguredSaleRuleThresholdAmount!.Value)
        {
            mismatches.Add(new OfflineArithmeticMismatch("configuredSaleRuleThresholdAmount", sale.ConfiguredSaleRuleThresholdAmount, expected.EligibleSubtotal, Codes.RuleThresholdNotMet));
        }

        for (var i = 0; i < sale.Items.Count; i++)
            CompareLine(i, sale.Items[i], expected.Lines[i], mismatches);

        Compare("subtotalBeforeDiscount", sale.SubtotalBeforeDiscount, expected.SubtotalBeforeDiscount, mismatches);
        if (!expected.AcceptedTotalsBeforeDiscount.Contains(sale.TotalBeforeDiscount))
            mismatches.Add(new OfflineArithmeticMismatch("totalBeforeDiscount", sale.TotalBeforeDiscount, expected.AcceptedTotalsBeforeDiscount[0], Codes.AmountMismatch));
        Compare("totalDiscountAmount", sale.TotalDiscountAmount, expected.TotalDiscountAmount, mismatches);
        Compare("totalTaxAmount", sale.TotalTaxAmount, expected.TotalTaxAmount, mismatches);
        Compare("totalAmount", sale.TotalAmount, expected.TotalAmount, mismatches);

        return new OfflineArithmeticVerdict(contract, mismatches);
    }

    private static void CompareLine(int index, OfflineSaleSyncLineCommand line, OfflineExpectedLine expected, List<OfflineArithmeticMismatch> mismatches)
    {
        var prefix = $"items[{index}].";
        Compare(prefix + "preTaxAmountBeforeDiscount", line.PreTaxAmountBeforeDiscount, expected.PreTaxAmount, mismatches);
        if (line.ItemDiscountAmount != expected.ItemDiscountAmount)
        {
            var code = line.ItemDiscountAmount > expected.PreTaxAmount ? Codes.DiscountExceedsBasis : Codes.AmountMismatch;
            mismatches.Add(new OfflineArithmeticMismatch(prefix + "itemDiscountAmount", line.ItemDiscountAmount, expected.ItemDiscountAmount, code));
        }

        if (line.SaleDiscountAmount != expected.SaleDiscountAmount)
        {
            var code = line.SaleDiscountAmount > expected.TaxableBeforeSaleDiscount ? Codes.DiscountExceedsBasis : Codes.AmountMismatch;
            mismatches.Add(new OfflineArithmeticMismatch(prefix + "saleDiscountAmount", line.SaleDiscountAmount, expected.SaleDiscountAmount, code));
        }

        Compare(prefix + "taxableAmount", line.TaxableAmount, expected.TaxableAmount, mismatches);
        Compare(prefix + "taxAmount", line.TaxAmount, expected.TaxAmount, mismatches);
        Compare(prefix + "totalAmount", line.TotalAmount, expected.TotalAmount, mismatches);
    }

    private static void Compare(string path, decimal supplied, decimal expected, List<OfflineArithmeticMismatch> mismatches)
    {
        if (supplied != expected)
            mismatches.Add(new OfflineArithmeticMismatch(path, supplied, expected, Codes.AmountMismatch));
    }

    private static void CheckInputs(OfflineSaleSyncCommand sale, bool isVersion1, List<OfflineArithmeticMismatch> mismatches)
    {
        if (!Enum.IsDefined(sale.PaymentMethod))
            mismatches.Add(new OfflineArithmeticMismatch("paymentMethod", (int)sale.PaymentMethod, null, Codes.UnsupportedValue));

        CheckDiscountInput("saleDiscountOverride", sale.SaleDiscountOverrideType, sale.SaleDiscountOverrideValue, isVersion1, mismatches);

        if (sale.ConfiguredSaleRuleId.HasValue)
        {
            if (sale.ConfiguredSaleRuleType is not (DiscountRuleType.SalePercentage or DiscountRuleType.SaleThresholdPercentage))
            {
                var supplied = sale.ConfiguredSaleRuleType is { } type ? (int)type : (decimal?)null;
                mismatches.Add(new OfflineArithmeticMismatch("configuredSaleRuleType", supplied, null, sale.ConfiguredSaleRuleType is null ? Codes.RuleEvidenceMissing : Codes.UnsupportedValue));
            }

            CheckRulePercentage("configuredSaleRulePercentage", sale.ConfiguredSaleRulePercentage, isVersion1, mismatches);

            if (sale.ConfiguredSaleRuleType == DiscountRuleType.SaleThresholdPercentage)
            {
                if (sale.ConfiguredSaleRuleThresholdAmount is not { } threshold)
                    mismatches.Add(new OfflineArithmeticMismatch("configuredSaleRuleThresholdAmount", null, null, Codes.RuleEvidenceMissing));
                else
                    CheckRange("configuredSaleRuleThresholdAmount", threshold, 0m, MaxMoneyInput, isVersion1 ? 2 : null, mismatches);
            }
        }

        for (var i = 0; i < sale.Items.Count; i++)
        {
            var line = sale.Items[i];
            var prefix = $"items[{i}].";
            if (line.Quantity <= 0m)
                mismatches.Add(new OfflineArithmeticMismatch(prefix + "quantity", line.Quantity, null, Codes.UnsupportedValue));
            else
                CheckRange(prefix + "quantity", line.Quantity, 0m, MaxQuantity, isVersion1 ? 3 : null, mismatches);
            CheckRange(prefix + "salesPrice", line.SalesPrice, 0m, MaxMoneyInput, isVersion1 ? 2 : null, mismatches);
            if (line.Quantity is > 0m and <= MaxQuantity
                && line.SalesPrice is >= 0m and <= MaxMoneyInput
                && line.SalesPrice * line.Quantity > MaxMoneyInput)
                mismatches.Add(new OfflineArithmeticMismatch($"items[{i}]", line.SalesPrice * line.Quantity, null, Codes.UnsupportedValue));
            CheckRange(prefix + "taxRatePercent", line.TaxRatePercent, 0m, 100m, isVersion1 ? 2 : null, mismatches);
            if (line.IsServiceLine) continue;

            CheckDiscountInput(prefix + "itemDiscountOverride", line.ItemDiscountOverrideType, line.ItemDiscountOverrideValue, isVersion1, mismatches);
            if (line.ConfiguredBatchRuleId.HasValue)
                CheckRulePercentage(prefix + "configuredBatchRulePercentage", line.ConfiguredBatchRulePercentage, isVersion1, mismatches);
        }
    }

    private static void CheckDiscountInput(string pathStem, InstantDiscountType type, decimal value, bool isVersion1, List<OfflineArithmeticMismatch> mismatches)
    {
        if (!Enum.IsDefined(type))
        {
            mismatches.Add(new OfflineArithmeticMismatch(pathStem + "Type", (int)type, null, Codes.UnsupportedValue));
            return;
        }

        var valuePath = pathStem + "Value";
        switch (type)
        {
            case InstantDiscountType.Percentage:
                CheckRange(valuePath, value, 0m, 100m, isVersion1 ? 2 : null, mismatches);
                break;
            case InstantDiscountType.Flat:
                CheckRange(valuePath, value, 0m, MaxMoneyInput, isVersion1 ? 2 : null, mismatches);
                break;
        }
    }

    private static void CheckRulePercentage(string path, decimal? percentage, bool isVersion1, List<OfflineArithmeticMismatch> mismatches)
    {
        if (percentage is not { } value)
        {
            mismatches.Add(new OfflineArithmeticMismatch(path, null, null, Codes.RuleEvidenceMissing));
            return;
        }

        CheckRange(path, value, 0m, 100m, isVersion1 ? 2 : null, mismatches);
    }

    private static void CheckRange(string path, decimal value, decimal min, decimal max, int? scale, List<OfflineArithmeticMismatch> mismatches)
    {
        if (value < min || value > max)
            mismatches.Add(new OfflineArithmeticMismatch(path, value, null, Codes.UnsupportedValue));
        else if (scale is { } places && !OfflineInvoiceArithmeticV1.FitsScale(value, places))
            mismatches.Add(new OfflineArithmeticMismatch(path, value, decimal.Round(value, places, MidpointRounding.AwayFromZero), Codes.ScaleExceeded));
    }
}
