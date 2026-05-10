using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.Services.Returns;

internal sealed class SaleReturnCalculator : ISaleReturnCalculator
{
    public SaleReturnCalculationResult Calculate(SaleReturnCalculationRequest request)
    {
        var warnings = new List<SaleReturnCalculationWarning>();
        var lines = request.Lines
            .Select(line => CalculateLine(line, warnings))
            .ToList();

        var totalRefundAmount = RoundMoney(lines.Sum(line => line.ApprovedRefundAmount));
        var totalTaxableAmount = RoundMoney(lines.Sum(line => line.TaxableAmount));
        var totalTaxAmount = RoundMoney(lines.Sum(line => line.TaxAmount));

        var dueFirstReduction = RoundMoney(Math.Min(totalRefundAmount, Math.Max(0m, request.OutstandingDueAmount)));
        var dueReductionAmount = dueFirstReduction;

        if (request.DueReductionOverrideAmount.HasValue)
        {
            var requestedDueReduction = RoundMoney(request.DueReductionOverrideAmount.Value);
            var maxDueReduction = RoundMoney(Math.Min(totalRefundAmount, Math.Max(0m, request.OutstandingDueAmount)));
            dueReductionAmount = RoundMoney(Math.Clamp(requestedDueReduction, 0m, maxDueReduction));

            if (dueReductionAmount != dueFirstReduction)
            {
                warnings.Add(new SaleReturnCalculationWarning(
                    "sale_return.due_override",
                    "Due reduction override differs from the default due-first split.",
                    SaleReturnWarningSeverity.Warning));
            }

            if (requestedDueReduction > maxDueReduction)
            {
                warnings.Add(new SaleReturnCalculationWarning(
                    "sale_return.due_override_exceeds_outstanding",
                    "Due reduction override exceeds current outstanding due.",
                    SaleReturnWarningSeverity.Warning));
            }

            var payoutAmountAfterOverride = RoundMoney(totalRefundAmount - dueReductionAmount);
            var outstandingDueAfterOverride = RoundMoney(Math.Max(0m, request.OutstandingDueAmount - dueReductionAmount));
            if (payoutAmountAfterOverride > 0m
                && outstandingDueAfterOverride > 0m
                && string.IsNullOrWhiteSpace(request.DueOverrideReason))
            {
                warnings.Add(new SaleReturnCalculationWarning(
                    "sale_return.note_required.due_override",
                    "Add a reason for overriding the default due-first split.",
                    SaleReturnWarningSeverity.Warning));
            }
        }

        var payoutAmount = RoundMoney(totalRefundAmount - dueReductionAmount);
        decimal? customerBalanceAfter = request.CustomerBalanceBefore.HasValue
            ? RoundMoney(Math.Max(0m, request.CustomerBalanceBefore.Value - dueReductionAmount))
            : null;

        return new SaleReturnCalculationResult(
            lines,
            totalRefundAmount,
            dueReductionAmount,
            payoutAmount,
            totalTaxableAmount,
            totalTaxAmount,
            request.CustomerBalanceBefore.HasValue ? RoundMoney(request.CustomerBalanceBefore.Value) : null,
            customerBalanceAfter,
            warnings);
    }

    private static SaleReturnLineCalculation CalculateLine(
        SaleReturnLineCalculationRequest line,
        List<SaleReturnCalculationWarning> warnings)
    {
        var ratio = line.OriginalSaleItemQuantity <= 0m
            ? 0m
            : line.Quantity / line.OriginalSaleItemQuantity;
        ratio = Math.Clamp(ratio, 0m, 1m);

        var taxableAmount = RoundMoney(line.OriginalPaidTaxableAmount * ratio);
        var taxAmount = RoundMoney(line.OriginalPaidTaxAmount * ratio);

        var discountedMaxRefundAmount = RoundMoney(line.OriginalPaidTotalAmount * ratio);
        var grossOriginalValue = RoundMoney(line.Quantity * line.OriginalSalesPrice);
        var taxAmountBeforeDiscount = CalculateTaxAmount(
            grossOriginalValue,
            line.OriginalTaxRatePercent,
            line.OriginalIsPriceIncludingTax);
        var maxRefundOverrideAmount = line.OriginalIsPriceIncludingTax
            ? grossOriginalValue
            : RoundMoney(grossOriginalValue + taxAmountBeforeDiscount);

        var requestedRefund = line.ApprovedRefundAmount ?? discountedMaxRefundAmount;
        var approvedRefundAmount = RoundMoney(Math.Clamp(requestedRefund, 0m, maxRefundOverrideAmount));
        var notes = NormalizeOptional(line.Notes);

        AddRequiredNoteWarnings(line, discountedMaxRefundAmount, approvedRefundAmount, notes, warnings);
        AddRefundOverrideWarnings(discountedMaxRefundAmount, approvedRefundAmount, notes, warnings);

        var maxRefundAmount = discountedMaxRefundAmount;

        return new SaleReturnLineCalculation(
            line.SaleItemId,
            line.Quantity,
            line.Condition,
            RoundMoney(line.OriginalCostPrice),
            RoundMoney(line.OriginalSalesPrice),
            line.OriginalTaxRatePercent,
            line.OriginalIsPriceIncludingTax,
            maxRefundAmount,
            approvedRefundAmount,
            taxableAmount,
            taxAmount,
            notes);
    }

    private static decimal CalculateTaxAmount(decimal grossOriginalValue, decimal taxRatePercent, bool priceIncludingTax)
    {
        if (taxRatePercent <= 0)
        {
            return 0m;
        }

        var taxAmount = priceIncludingTax
            ? grossOriginalValue * taxRatePercent / (100m + taxRatePercent)
            : grossOriginalValue * taxRatePercent / 100m;

        return RoundMoney(taxAmount);
    }

    private static void AddRequiredNoteWarnings(
        SaleReturnLineCalculationRequest line,
        decimal maxRefundAmount,
        decimal approvedRefundAmount,
        string? notes,
        List<SaleReturnCalculationWarning> warnings)
    {
        if (!string.IsNullOrWhiteSpace(notes))
        {
            return;
        }

        if (line.Condition == SaleReturnCondition.Wastage)
        {
            warnings.Add(new SaleReturnCalculationWarning(
                "sale_return.note_required.wastage",
                "Add a note for wastage returns.",
                SaleReturnWarningSeverity.Warning));
        }

        if (approvedRefundAmount == 0m)
        {
            warnings.Add(new SaleReturnCalculationWarning(
                "sale_return.note_required.zero_refund",
                "Add a note when approving a zero refund.",
                SaleReturnWarningSeverity.Warning));
            return;
        }

        if (approvedRefundAmount < maxRefundAmount)
        {
            warnings.Add(new SaleReturnCalculationWarning(
                "sale_return.note_required.partial_refund",
                "Add a note when approving a partial refund.",
                SaleReturnWarningSeverity.Warning));
        }
    }

    private static void AddRefundOverrideWarnings(
        decimal maxRefundAmount,
        decimal approvedRefundAmount,
        string? notes,
        List<SaleReturnCalculationWarning> warnings)
    {
        if (approvedRefundAmount <= maxRefundAmount)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(notes))
        {
            return;
        }

        warnings.Add(new SaleReturnCalculationWarning(
            "sale_return.note_required.refund_override",
            "Add a note when approving a refund above the discounted paid amount.",
            SaleReturnWarningSeverity.Warning));
    }

    private static decimal RoundMoney(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
