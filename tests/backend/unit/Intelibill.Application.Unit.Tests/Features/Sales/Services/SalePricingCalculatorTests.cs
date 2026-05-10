using Intelibill.Application.Features.Sales.Services.Pricing;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Services;

public class SalePricingCalculatorTests
{
    private readonly SalePricingCalculator _calculator = new();

    [Fact]
    public void Calculate_WhenTaxExclusive_ComputesTaxAfterDiscounts()
    {
        var result = _calculator.Calculate(new SalePricingCalculationRequest(
            [
                Line(quantity: 2m, salesPrice: 100m, taxRatePercent: 18m, taxIncluded: false),
            ],
            SaleDiscount: None()));

        Assert.False(result.IsError);
        var line = Assert.Single(result.Value.Lines);
        Assert.Equal(200m, line.PreTaxAmountBeforeDiscount);
        Assert.Equal(0m, line.ItemDiscountAmount);
        Assert.Equal(0m, line.SaleDiscountAmount);
        Assert.Equal(200m, line.TaxableAmount);
        Assert.Equal(36m, line.TaxAmount);
        Assert.Equal(236m, line.TotalAmount);
        Assert.Equal(236m, result.Value.TotalAmount);
    }

    [Fact]
    public void Calculate_WhenTaxIncluded_ExtractsPreTaxBeforeDiscount()
    {
        var result = _calculator.Calculate(new SalePricingCalculationRequest(
            [
                Line(quantity: 2m, salesPrice: 118m, taxRatePercent: 18m, taxIncluded: true),
            ],
            SaleDiscount: None()));

        Assert.False(result.IsError);
        var line = Assert.Single(result.Value.Lines);
        Assert.Equal(200m, line.PreTaxAmountBeforeDiscount);
        Assert.Equal(36m, line.TaxAmount);
        Assert.Equal(236m, line.TotalAmount);
    }

    [Fact]
    public void Calculate_AppliesItemPercentageDiscountBeforeTax()
    {
        var result = _calculator.Calculate(new SalePricingCalculationRequest(
            [
                Line(quantity: 1m, salesPrice: 100m, taxRatePercent: 18m, taxIncluded: false, costPrice: 70m,
                    itemDiscount: new InstantDiscount(InstantDiscountType.Percentage, 10m)),
            ],
            SaleDiscount: None()));

        Assert.False(result.IsError);
        var line = Assert.Single(result.Value.Lines);
        Assert.Equal(100m, line.PreTaxAmountBeforeDiscount);
        Assert.Equal(10m, line.ItemDiscountAmount);
        Assert.Equal(90m, line.TaxableAmount);
        Assert.Equal(16.2m, line.TaxAmount);
        Assert.Equal(106.2m, line.TotalAmount);
    }

    [Fact]
    public void Calculate_RejectsItemDiscountThatWouldGoBelowCost()
    {
        var result = _calculator.Calculate(new SalePricingCalculationRequest(
            [
                Line(quantity: 1m, salesPrice: 100m, taxRatePercent: 0m, taxIncluded: false, costPrice: 95m,
                    itemDiscount: new InstantDiscount(InstantDiscountType.Flat, 10m)),
            ],
            SaleDiscount: None()));

        Assert.True(result.IsError);
    }

    [Fact]
    public void Calculate_ReturnsMaxAllowedItemDiscountsPerLine()
    {
        var result = _calculator.Calculate(new SalePricingCalculationRequest(
            [
                Line(quantity: 1m, salesPrice: 100m, taxRatePercent: 0m, taxIncluded: false, costPrice: 70m),
            ],
            SaleDiscount: None()));

        Assert.False(result.IsError);
        var line = Assert.Single(result.Value.Lines);
        Assert.Equal(30m, line.MaxAllowedItemDiscountFlat);
        Assert.Equal(30m, line.MaxAllowedItemDiscountPercent);
    }

    [Fact]
    public void Calculate_RoundsLineMonetaryValuesBeforeSummingTotals()
    {
        var result = _calculator.Calculate(new SalePricingCalculationRequest(
            [
                Line(inventoryBatchId: Guid.NewGuid(), quantity: 0.333m, salesPrice: 10m, taxRatePercent: 0m, taxIncluded: false, costPrice: 0m),
                Line(inventoryBatchId: Guid.NewGuid(), quantity: 0.333m, salesPrice: 10m, taxRatePercent: 0m, taxIncluded: false, costPrice: 0m),
                Line(inventoryBatchId: Guid.NewGuid(), quantity: 0.333m, salesPrice: 10m, taxRatePercent: 0m, taxIncluded: false, costPrice: 0m),
            ],
            SaleDiscount: None()));

        Assert.False(result.IsError);
        Assert.All(result.Value.Lines, l => Assert.Equal(3.33m, l.PreTaxAmountBeforeDiscount));
        Assert.Equal(9.99m, result.Value.TotalAmount);
    }

    [Fact]
    public void Calculate_ReturnsEligibleSubtotalForSaleDiscount()
    {
        var eligibleBatch = Guid.NewGuid();
        var ineligibleBatch = Guid.NewGuid();

        var result = _calculator.Calculate(new SalePricingCalculationRequest(
            [
                Line(inventoryBatchId: eligibleBatch, quantity: 1m, salesPrice: 100m, taxRatePercent: 0m, taxIncluded: false, costPrice: 70m),
                Line(inventoryBatchId: ineligibleBatch, quantity: 1m, salesPrice: 50m, taxRatePercent: 0m, taxIncluded: false, costPrice: 50m),
            ],
            SaleDiscount: None()));

        Assert.False(result.IsError);
        Assert.Equal(100m, result.Value.SaleLevelEligibleSubtotal);
    }

    [Fact]
    public void Calculate_WhenSaleDiscountRequestedAndNoEligibleLines_ReturnsError()
    {
        var result = _calculator.Calculate(new SalePricingCalculationRequest(
            [
                Line(quantity: 1m, salesPrice: 50m, taxRatePercent: 0m, taxIncluded: false, costPrice: 50m),
            ],
            SaleDiscount: new InstantDiscount(InstantDiscountType.Flat, 5m)));

        Assert.True(result.IsError);
    }

    [Fact]
    public void Calculate_WhenSaleDiscountPercentageRequestedAndNoEligibleLines_ReturnsError()
    {
        var result = _calculator.Calculate(new SalePricingCalculationRequest(
            [
                Line(quantity: 1m, salesPrice: 50m, taxRatePercent: 0m, taxIncluded: false, costPrice: 50m),
            ],
            SaleDiscount: new InstantDiscount(InstantDiscountType.Percentage, 10m)));

        Assert.True(result.IsError);
    }

    [Fact]
    public void Calculate_WhenSaleDiscountApplied_AllocatesAcrossEligibleLinesAndRecomputesTax()
    {
        var batch1 = Guid.NewGuid();
        var batch2 = Guid.NewGuid();

        var result = _calculator.Calculate(new SalePricingCalculationRequest(
            [
                Line(inventoryBatchId: batch1, quantity: 1m, salesPrice: 100m, taxRatePercent: 18m, taxIncluded: false, costPrice: 70m),
                Line(inventoryBatchId: batch2, quantity: 1m, salesPrice: 100m, taxRatePercent: 18m, taxIncluded: false, costPrice: 70m),
            ],
            SaleDiscount: new InstantDiscount(InstantDiscountType.Flat, 10m)));

        Assert.False(result.IsError);
        Assert.Equal(200m, result.Value.SaleLevelEligibleSubtotal);
        Assert.Equal(10m, result.Value.TotalDiscountAmount);
        Assert.Equal(34.2m, result.Value.TotalTaxAmount);
        Assert.Equal(224.2m, result.Value.TotalAmount);

        var line1 = result.Value.Lines.Single(l => l.InventoryBatchId == batch1);
        var line2 = result.Value.Lines.Single(l => l.InventoryBatchId == batch2);
        Assert.Equal(190m, line1.TaxableAmount + line2.TaxableAmount);
        Assert.Equal(10m, line1.SaleDiscountAmount + line2.SaleDiscountAmount);
    }

    [Fact]
    public void Calculate_WhenMultipleLinesShareSameInventoryBatchId_AllocatesSaleDiscountPerLine()
    {
        var sharedBatchId = Guid.NewGuid();

        var result = _calculator.Calculate(new SalePricingCalculationRequest(
            [
                Line(inventoryBatchId: sharedBatchId, quantity: 1m, salesPrice: 50m, taxRatePercent: 0m, taxIncluded: false, costPrice: 40m),
                Line(inventoryBatchId: sharedBatchId, quantity: 1m, salesPrice: 150m, taxRatePercent: 0m, taxIncluded: false, costPrice: 50m),
            ],
            SaleDiscount: new InstantDiscount(InstantDiscountType.Flat, 20m)));

        Assert.False(result.IsError);
        Assert.Equal(200m, result.Value.SaleLevelEligibleSubtotal);
        Assert.Equal(20m, result.Value.TotalDiscountAmount);

        Assert.Equal(2, result.Value.Lines.Count);
        var line1 = result.Value.Lines[0];
        var line2 = result.Value.Lines[1];
        Assert.Equal(sharedBatchId, line1.InventoryBatchId);
        Assert.Equal(sharedBatchId, line2.InventoryBatchId);

        Assert.Equal(5m, line1.SaleDiscountAmount);
        Assert.Equal(15m, line2.SaleDiscountAmount);
        Assert.Equal(45m, line1.TaxableAmount);
        Assert.Equal(135m, line2.TaxableAmount);
        Assert.Equal(180m, result.Value.TotalTaxableAmount);
    }

    [Fact]
    public void Calculate_WhenSaleDiscountExceedsBelowCostCapacity_ReturnsError()
    {
        var result = _calculator.Calculate(new SalePricingCalculationRequest(
            [
                Line(quantity: 1m, salesPrice: 100m, taxRatePercent: 0m, taxIncluded: false, costPrice: 99m),
                Line(quantity: 1m, salesPrice: 100m, taxRatePercent: 0m, taxIncluded: false, costPrice: 99m),
            ],
            SaleDiscount: new InstantDiscount(InstantDiscountType.Flat, 5m)));

        Assert.True(result.IsError);
    }

    private static SalePricingLineCalculationRequest Line(
        Guid? inventoryBatchId = null,
        decimal quantity = 1m,
        decimal costPrice = 0m,
        decimal salesPrice = 100m,
        decimal mrp = 100m,
        decimal taxRatePercent = 0m,
        bool taxIncluded = false,
        InstantDiscount? itemDiscount = null) =>
        new(
            inventoryBatchId ?? Guid.NewGuid(),
            quantity,
            costPrice,
            salesPrice,
            mrp,
            taxRatePercent,
            taxIncluded,
            itemDiscount ?? None());

    private static InstantDiscount None() => new(InstantDiscountType.None, 0m);
}
