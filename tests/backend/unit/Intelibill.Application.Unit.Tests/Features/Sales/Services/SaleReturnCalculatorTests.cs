using Intelibill.Application.Features.Sales.Services.Returns;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Services;

public class SaleReturnCalculatorTests
{
    private readonly SaleReturnCalculator _calculator = new();

    [Fact]
    public void Calculate_WhenTaxExclusive_UsesOriginalSaleValueForMaxRefundAndTax()
    {
        var result = _calculator.Calculate(new SaleReturnCalculationRequest(
            [Line(quantity: 2m, salesPrice: 100m, taxRatePercent: 18m, taxIncluded: false)],
            OutstandingDueAmount: 0m,
            CustomerBalanceBefore: null));

        var line = Assert.Single(result.Lines);
        Assert.Equal(236m, line.MaxRefundAmount);
        Assert.Equal(200m, line.TaxableAmount);
        Assert.Equal(36m, line.TaxAmount);
        Assert.Equal(236m, line.ApprovedRefundAmount);
        Assert.Equal(236m, result.TotalRefundAmount);
        Assert.Equal(200m, result.TotalTaxableAmount);
        Assert.Equal(36m, result.TotalTaxAmount);
    }

    [Fact]
    public void Calculate_WhenTaxIncluded_ExtractsTaxFromOriginalSaleValue()
    {
        var result = _calculator.Calculate(new SaleReturnCalculationRequest(
            [Line(quantity: 2m, salesPrice: 118m, taxRatePercent: 18m, taxIncluded: true)],
            OutstandingDueAmount: 0m,
            CustomerBalanceBefore: null));

        var line = Assert.Single(result.Lines);
        Assert.Equal(236m, line.MaxRefundAmount);
        Assert.Equal(200m, line.TaxableAmount);
        Assert.Equal(36m, line.TaxAmount);
    }

    [Fact]
    public void Calculate_DefaultsApprovedRefundToMax()
    {
        var result = _calculator.Calculate(new SaleReturnCalculationRequest(
            [Line(quantity: 1m, salesPrice: 100m, taxRatePercent: 18m, taxIncluded: false)],
            OutstandingDueAmount: 0m,
            CustomerBalanceBefore: null));

        Assert.Equal(result.Lines[0].MaxRefundAmount, result.Lines[0].ApprovedRefundAmount);
    }

    [Fact]
    public void Calculate_AcceptsPartialAndZeroRefunds()
    {
        var result = _calculator.Calculate(new SaleReturnCalculationRequest(
            [
                Line(approvedRefundAmount: 50m, notes: "Damaged packaging"),
                Line(approvedRefundAmount: 0m, notes: "Outside refund policy"),
            ],
            OutstandingDueAmount: 0m,
            CustomerBalanceBefore: null));

        Assert.Equal(50m, result.Lines[0].ApprovedRefundAmount);
        Assert.Equal(0m, result.Lines[1].ApprovedRefundAmount);
        Assert.Equal(50m, result.TotalRefundAmount);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Calculate_CapsApprovedRefundAtMax()
    {
        var result = _calculator.Calculate(new SaleReturnCalculationRequest(
            [Line(approvedRefundAmount: 999m)],
            OutstandingDueAmount: 0m,
            CustomerBalanceBefore: null));

        var line = Assert.Single(result.Lines);
        Assert.Equal(line.MaxRefundAmount, line.ApprovedRefundAmount);
    }

    [Fact]
    public void Calculate_TaxValuesUseOriginalSaleValueForPartialRefund()
    {
        var result = _calculator.Calculate(new SaleReturnCalculationRequest(
            [Line(quantity: 1m, salesPrice: 100m, taxRatePercent: 18m, taxIncluded: false, approvedRefundAmount: 50m, notes: "Goodwill")],
            OutstandingDueAmount: 0m,
            CustomerBalanceBefore: null));

        var line = Assert.Single(result.Lines);
        Assert.Equal(50m, line.ApprovedRefundAmount);
        Assert.Equal(100m, line.TaxableAmount);
        Assert.Equal(18m, line.TaxAmount);
    }

    [Fact]
    public void Calculate_RoundsMoneyAwayFromZero()
    {
        var result = _calculator.Calculate(new SaleReturnCalculationRequest(
            [Line(quantity: 1m, salesPrice: 0.05m, taxRatePercent: 10m, taxIncluded: false, approvedRefundAmount: 0.005m, notes: "Rounding check")],
            OutstandingDueAmount: 0m,
            CustomerBalanceBefore: 1.005m));

        var line = Assert.Single(result.Lines);
        Assert.Equal(0.01m, line.TaxAmount);
        Assert.Equal(0.06m, line.MaxRefundAmount);
        Assert.Equal(0.01m, line.ApprovedRefundAmount);
        Assert.Equal(1.01m, result.CustomerBalanceBefore);
    }

    [Fact]
    public void Calculate_DefaultSplitReducesDueFirstAndProjectsCustomerBalance()
    {
        var result = _calculator.Calculate(new SaleReturnCalculationRequest(
            [Line(quantity: 1m, salesPrice: 100m, taxRatePercent: 0m, taxIncluded: true)],
            OutstandingDueAmount: 70m,
            CustomerBalanceBefore: 90m));

        Assert.Equal(100m, result.TotalRefundAmount);
        Assert.Equal(70m, result.DueReductionAmount);
        Assert.Equal(30m, result.PayoutAmount);
        Assert.Equal(90m, result.CustomerBalanceBefore);
        Assert.Equal(20m, result.CustomerBalanceAfter);
    }

    [Fact]
    public void Calculate_AddsWarningWhenDueOverrideDiffersFromDefault()
    {
        var result = _calculator.Calculate(new SaleReturnCalculationRequest(
            [Line(quantity: 1m, salesPrice: 100m, taxRatePercent: 0m, taxIncluded: true)],
            OutstandingDueAmount: 70m,
            CustomerBalanceBefore: 90m,
            DueReductionOverrideAmount: 25m,
            DueOverrideReason: "Customer service exception"));

        Assert.Equal(25m, result.DueReductionAmount);
        Assert.Equal(75m, result.PayoutAmount);
        Assert.Equal(65m, result.CustomerBalanceAfter);
        Assert.Contains(result.Warnings, warning =>
            warning.Code == "sale_return.due_override"
            && warning.Severity == SaleReturnWarningSeverity.Warning);
    }

    [Fact]
    public void Calculate_AddsWarningWhenDueOverrideReasonIsMissing()
    {
        var result = _calculator.Calculate(new SaleReturnCalculationRequest(
            [Line(quantity: 1m, salesPrice: 100m, taxRatePercent: 0m, taxIncluded: true)],
            OutstandingDueAmount: 70m,
            CustomerBalanceBefore: 90m,
            DueReductionOverrideAmount: 25m));

        Assert.Contains(result.Warnings, warning =>
            warning.Code == "sale_return.note_required.due_override"
            && warning.Severity == SaleReturnWarningSeverity.Warning);
    }

    [Fact]
    public void Calculate_DoesNotRequireDueOverrideReasonWhenOverrideLeavesNoDue()
    {
        var result = _calculator.Calculate(new SaleReturnCalculationRequest(
            [Line(quantity: 1m, salesPrice: 100m, taxRatePercent: 0m, taxIncluded: true)],
            OutstandingDueAmount: 70m,
            CustomerBalanceBefore: 70m,
            DueReductionOverrideAmount: 70m));

        Assert.DoesNotContain(result.Warnings, warning =>
            warning.Code == "sale_return.note_required.due_override");
    }

    [Fact]
    public void Calculate_ClampsDueOverrideToOutstandingDueAndWarns()
    {
        var result = _calculator.Calculate(new SaleReturnCalculationRequest(
            [Line(quantity: 1m, salesPrice: 100m, taxRatePercent: 0m, taxIncluded: true)],
            OutstandingDueAmount: 70m,
            CustomerBalanceBefore: 70m,
            DueReductionOverrideAmount: 90m,
            DueOverrideReason: "Customer wants due cleared first"));

        Assert.Equal(70m, result.DueReductionAmount);
        Assert.Equal(30m, result.PayoutAmount);
        Assert.Equal(0m, result.CustomerBalanceAfter);
        Assert.Contains(result.Warnings, warning =>
            warning.Code == "sale_return.due_override_exceeds_outstanding"
            && warning.Severity == SaleReturnWarningSeverity.Warning);
    }

    [Fact]
    public void Calculate_AddsRequiredNoteWarnings()
    {
        var result = _calculator.Calculate(new SaleReturnCalculationRequest(
            [
                Line(condition: SaleReturnCondition.Wastage),
                Line(approvedRefundAmount: 50m),
                Line(approvedRefundAmount: 0m),
            ],
            OutstandingDueAmount: 0m,
            CustomerBalanceBefore: null));

        Assert.Contains(result.Warnings, warning =>
            warning.Code == "sale_return.note_required.wastage"
            && warning.Severity == SaleReturnWarningSeverity.Warning);
        Assert.Contains(result.Warnings, warning =>
            warning.Code == "sale_return.note_required.partial_refund"
            && warning.Severity == SaleReturnWarningSeverity.Warning);
        Assert.Contains(result.Warnings, warning =>
            warning.Code == "sale_return.note_required.zero_refund"
            && warning.Severity == SaleReturnWarningSeverity.Warning);
    }

    private static SaleReturnLineCalculationRequest Line(
        decimal quantity = 1m,
        decimal salesPrice = 100m,
        decimal taxRatePercent = 18m,
        bool taxIncluded = false,
        SaleReturnCondition condition = SaleReturnCondition.Restockable,
        decimal? approvedRefundAmount = null,
        string? notes = null)
    {
        var gross = decimal.Round(salesPrice * quantity, 2, MidpointRounding.AwayFromZero);
        var taxAmount = taxRatePercent <= 0m
            ? 0m
            : taxIncluded
                ? decimal.Round(gross * taxRatePercent / (100m + taxRatePercent), 2, MidpointRounding.AwayFromZero)
                : decimal.Round(gross * taxRatePercent / 100m, 2, MidpointRounding.AwayFromZero);
        var taxableAmount = taxIncluded
            ? decimal.Round(gross - taxAmount, 2, MidpointRounding.AwayFromZero)
            : gross;
        var totalAmount = taxIncluded
            ? gross
            : decimal.Round(taxableAmount + taxAmount, 2, MidpointRounding.AwayFromZero);

        return new SaleReturnLineCalculationRequest(
            Guid.NewGuid(),
            quantity,
            OriginalCostPrice: 80m,
            salesPrice,
            taxRatePercent,
            taxIncluded,
            OriginalSaleItemQuantity: quantity,
            OriginalPaidTaxableAmount: taxableAmount,
            OriginalPaidTaxAmount: taxAmount,
            OriginalPaidTotalAmount: totalAmount,
            condition,
            approvedRefundAmount,
            notes);
    }
}
