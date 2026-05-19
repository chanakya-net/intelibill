using Intelibill.Application.Features.Sales.Services.Pricing;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Services;

public class SalePricingCalculatorTests
{
    private static readonly Guid ShopId = Guid.NewGuid();
    private static readonly DateTimeOffset SaleTime = new(2026, 05, 10, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Calculate_WhenTaxExclusive_ComputesTaxAfterDiscounts()
    {
        var calculator = CreateCalculatorWithRules([]);

        var result = await calculator.CalculateAsync(new SalePricingCalculationRequest(
            ShopId,
            SaleTime,
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
    public async Task Calculate_WhenTaxIncluded_ExtractsPreTaxBeforeDiscount()
    {
        var calculator = CreateCalculatorWithRules([]);

        var result = await calculator.CalculateAsync(new SalePricingCalculationRequest(
            ShopId,
            SaleTime,
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
    public async Task Calculate_WhenTaxIncludedAndGrossPriceIsAboveCost_AllowsSale()
    {
        var calculator = CreateCalculatorWithRules([]);

        var result = await calculator.CalculateAsync(new SalePricingCalculationRequest(
            ShopId,
            SaleTime,
            [
                Line(quantity: 1m, salesPrice: 24m, taxRatePercent: 40m, taxIncluded: true, costPrice: 18m),
            ],
            SaleDiscount: None()));

        Assert.False(result.IsError);
        var line = Assert.Single(result.Value.Lines);
        Assert.Equal(17.14m, line.PreTaxAmountBeforeDiscount);
        Assert.Equal(6.86m, line.TaxAmount);
        Assert.Equal(24m, line.TotalAmount);
    }

    [Fact]
    public async Task Calculate_WhenTaxIncluded_PreservesGrossShelfPriceAfterTaxRounding()
    {
        var calculator = CreateCalculatorWithRules([]);

        var result = await calculator.CalculateAsync(new SalePricingCalculationRequest(
            ShopId,
            SaleTime,
            [
                Line(quantity: 19m, salesPrice: 24m, taxRatePercent: 40m, taxIncluded: true, costPrice: 18m),
            ],
            SaleDiscount: None()));

        Assert.False(result.IsError);
        var line = Assert.Single(result.Value.Lines);
        Assert.Equal(325.71m, line.TaxableAmount);
        Assert.Equal(130.29m, line.TaxAmount);
        Assert.Equal(456m, line.TotalAmount);
        Assert.Equal(456m, result.Value.TotalAmount);
    }

    [Fact]
    public async Task Calculate_WhenTaxIncluded_ReturnsGrossAwareDiscountCapacity()
    {
        var calculator = CreateCalculatorWithRules([]);

        var result = await calculator.CalculateAsync(new SalePricingCalculationRequest(
            ShopId,
            SaleTime,
            [
                Line(quantity: 1m, salesPrice: 24m, taxRatePercent: 40m, taxIncluded: true, costPrice: 18m),
            ],
            SaleDiscount: None()));

        Assert.False(result.IsError);
        var line = Assert.Single(result.Value.Lines);
        Assert.Equal(4.29m, line.MaxAllowedItemDiscountFlat);
    }

    [Fact]
    public async Task Calculate_WhenTaxIncludedAndDiscountWouldMakeGrossBelowCost_ReturnsError()
    {
        var calculator = CreateCalculatorWithRules([]);

        var result = await calculator.CalculateAsync(new SalePricingCalculationRequest(
            ShopId,
            SaleTime,
            [
                Line(
                    quantity: 1m,
                    salesPrice: 24m,
                    taxRatePercent: 40m,
                    taxIncluded: true,
                    costPrice: 18m,
                    itemDiscount: new InstantDiscount(InstantDiscountType.Flat, 5m)),
            ],
            SaleDiscount: None()));

        Assert.True(result.IsError);
        Assert.Equal("SalePricing.ItemDiscountBelowCost", result.FirstError.Code);
    }

    [Fact]
    public async Task Calculate_WhenTaxIncludedAndGrossPriceIsBelowCost_ReturnsError()
    {
        var calculator = CreateCalculatorWithRules([]);

        var result = await calculator.CalculateAsync(new SalePricingCalculationRequest(
            ShopId,
            SaleTime,
            [
                Line(quantity: 1m, salesPrice: 24m, taxRatePercent: 40m, taxIncluded: true, costPrice: 25m),
            ],
            SaleDiscount: None()));

        Assert.True(result.IsError);
        Assert.Equal("SalePricing.BelowCost", result.FirstError.Code);
    }

    [Fact]
    public async Task Calculate_AppliesItemPercentageDiscountBeforeTax()
    {
        var calculator = CreateCalculatorWithRules([]);

        var result = await calculator.CalculateAsync(new SalePricingCalculationRequest(
            ShopId,
            SaleTime,
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
    public async Task Calculate_RejectsItemDiscountThatWouldGoBelowCost()
    {
        var calculator = CreateCalculatorWithRules([]);

        var result = await calculator.CalculateAsync(new SalePricingCalculationRequest(
            ShopId,
            SaleTime,
            [
                Line(quantity: 1m, salesPrice: 100m, taxRatePercent: 0m, taxIncluded: false, costPrice: 95m,
                    itemDiscount: new InstantDiscount(InstantDiscountType.Flat, 10m)),
            ],
            SaleDiscount: None()));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Calculate_ReturnsMaxAllowedItemDiscountsPerLine()
    {
        var calculator = CreateCalculatorWithRules([]);

        var result = await calculator.CalculateAsync(new SalePricingCalculationRequest(
            ShopId,
            SaleTime,
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
    public async Task Calculate_ReturnsEligibleSubtotalForSaleDiscount()
    {
        var eligibleBatch = Guid.NewGuid();
        var ineligibleBatch = Guid.NewGuid();
        var calculator = CreateCalculatorWithRules([]);

        var result = await calculator.CalculateAsync(new SalePricingCalculationRequest(
            ShopId,
            SaleTime,
            [
                Line(inventoryBatchId: eligibleBatch, quantity: 1m, salesPrice: 100m, taxRatePercent: 0m, taxIncluded: false, costPrice: 40m),
                Line(inventoryBatchId: ineligibleBatch, quantity: 1m, salesPrice: 50m, taxRatePercent: 0m, taxIncluded: false, costPrice: 50m),
            ],
            SaleDiscount: None()));

        Assert.False(result.IsError);
        Assert.Equal(100m, result.Value.SaleLevelEligibleSubtotal);
    }

    [Fact]
    public async Task Calculate_WhenSaleDiscountRequestedAndNoEligibleLines_ReturnsError()
    {
        var calculator = CreateCalculatorWithRules([]);

        var result = await calculator.CalculateAsync(new SalePricingCalculationRequest(
            ShopId,
            SaleTime,
            [
                Line(quantity: 1m, salesPrice: 50m, taxRatePercent: 0m, taxIncluded: false, costPrice: 50m),
            ],
            SaleDiscount: new InstantDiscount(InstantDiscountType.Flat, 5m)));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Calculate_WhenSaleDiscountPercentageRequestedAndNoEligibleLines_ReturnsError()
    {
        var calculator = CreateCalculatorWithRules([]);

        var result = await calculator.CalculateAsync(new SalePricingCalculationRequest(
            ShopId,
            SaleTime,
            [
                Line(quantity: 1m, salesPrice: 50m, taxRatePercent: 0m, taxIncluded: false, costPrice: 50m),
            ],
            SaleDiscount: new InstantDiscount(InstantDiscountType.Percentage, 10m)));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Calculate_WhenSaleDiscountApplied_AllocatesAcrossEligibleLinesAndRecomputesTax()
    {
        var batch1 = Guid.NewGuid();
        var batch2 = Guid.NewGuid();
        var calculator = CreateCalculatorWithRules([]);

        var result = await calculator.CalculateAsync(new SalePricingCalculationRequest(
            ShopId,
            SaleTime,
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
    public async Task Calculate_WhenMultipleLinesShareSameInventoryBatchId_AllocatesSaleDiscountPerLine()
    {
        var sharedBatchId = Guid.NewGuid();
        var calculator = CreateCalculatorWithRules([]);

        var result = await calculator.CalculateAsync(new SalePricingCalculationRequest(
            ShopId,
            SaleTime,
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
    public async Task Calculate_WhenSaleDiscountExceedsBelowCostCapacity_ReturnsError()
    {
        var calculator = CreateCalculatorWithRules([]);

        var result = await calculator.CalculateAsync(new SalePricingCalculationRequest(
            ShopId,
            SaleTime,
            [
                Line(quantity: 1m, salesPrice: 100m, taxRatePercent: 0m, taxIncluded: false, costPrice: 99m),
                Line(quantity: 1m, salesPrice: 100m, taxRatePercent: 0m, taxIncluded: false, costPrice: 99m),
            ],
            SaleDiscount: new InstantDiscount(InstantDiscountType.Flat, 5m)));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Calculate_AppliesConfiguredBatchPercentageDiscount_BeforeSaleEligibility()
    {
        var batchId = Guid.NewGuid();
        var configured = CreateRule(
            ruleType: DiscountRuleType.BatchPercentage,
            percentage: 10m,
            inventoryBatchId: batchId);

        var saleRule = CreateRule(
            ruleType: DiscountRuleType.SaleThresholdPercentage,
            percentage: 20m,
            thresholdAmount: 95m);

        var calculator = CreateCalculatorWithRules([configured, saleRule]);

        var result = await calculator.CalculateAsync(new SalePricingCalculationRequest(
            ShopId,
            SaleTime,
            [
                Line(inventoryBatchId: batchId, quantity: 1m, salesPrice: 100m, taxRatePercent: 0m, taxIncluded: false, costPrice: 0m),
            ],
            SaleDiscount: None()));

        Assert.False(result.IsError);
        var line = Assert.Single(result.Value.Lines);
        Assert.Equal(10m, line.ItemDiscountAmount);
        Assert.Equal(90m, result.Value.SaleLevelEligibleSubtotal);
        Assert.Null(result.Value.ConfiguredSaleRule);
    }

    [Fact]
    public async Task Calculate_ConfiguredBatchDiscount_CanBeReducedButNotIncreasedByOverride()
    {
        var batchId = Guid.NewGuid();
        var configured = CreateRule(
            ruleType: DiscountRuleType.BatchPercentage,
            percentage: 10m,
            inventoryBatchId: batchId);

        var calculator = CreateCalculatorWithRules([configured]);

        var increased = await calculator.CalculateAsync(new SalePricingCalculationRequest(
            ShopId,
            SaleTime,
            [
                Line(inventoryBatchId: batchId, quantity: 1m, salesPrice: 100m, taxRatePercent: 0m, taxIncluded: false, costPrice: 0m,
                    itemDiscount: new InstantDiscount(InstantDiscountType.Percentage, 20m)),
            ],
            SaleDiscount: None()));

        Assert.False(increased.IsError);
        Assert.Equal(10m, increased.Value.Lines[0].ItemDiscountAmount);

        var reduced = await calculator.CalculateAsync(new SalePricingCalculationRequest(
            ShopId,
            SaleTime,
            [
                Line(inventoryBatchId: batchId, quantity: 1m, salesPrice: 100m, taxRatePercent: 0m, taxIncluded: false, costPrice: 0m,
                    itemDiscount: new InstantDiscount(InstantDiscountType.Percentage, 5m)),
            ],
            SaleDiscount: None()));

        Assert.False(reduced.IsError);
        Assert.Equal(5m, reduced.Value.Lines[0].ItemDiscountAmount);
    }

    [Fact]
    public async Task Calculate_AppliesBestConfiguredSaleRule_AndCapsByOverride()
    {
        var batchId = Guid.NewGuid();
        var line = Line(inventoryBatchId: batchId, quantity: 1m, salesPrice: 200m, taxRatePercent: 0m, taxIncluded: false, costPrice: 0m);

        var sale10 = CreateRule(DiscountRuleType.SalePercentage, percentage: 10m);
        var sale15 = CreateRule(DiscountRuleType.SalePercentage, percentage: 15m);

        // Make sale15 the "newest" only if tie-breaker is needed; here it wins by discount amount anyway.
        var calculator = CreateCalculatorWithRules([sale10, sale15]);

        var noOverride = await calculator.CalculateAsync(new SalePricingCalculationRequest(
            ShopId,
            SaleTime,
            [line],
            SaleDiscount: None()));

        Assert.False(noOverride.IsError);
        Assert.NotNull(noOverride.Value.ConfiguredSaleRule);
        Assert.Equal(sale15.Id, noOverride.Value.ConfiguredSaleRule!.RuleId);
        Assert.Equal(30m, noOverride.Value.TotalDiscountAmount);

        var cappedByOverride = await calculator.CalculateAsync(new SalePricingCalculationRequest(
            ShopId,
            SaleTime,
            [line],
            SaleDiscount: new InstantDiscount(InstantDiscountType.Percentage, 5m)));

        Assert.False(cappedByOverride.IsError);
        Assert.Equal(10m, cappedByOverride.Value.TotalDiscountAmount);
    }

    [Fact]
    public async Task Calculate_SaleRuleSelection_TieBreaksByThresholdSpecificityThenNewest()
    {
        var batchId = Guid.NewGuid();
        var line = Line(inventoryBatchId: batchId, quantity: 1m, salesPrice: 100m, taxRatePercent: 0m, taxIncluded: false, costPrice: 0m);

        var percent10 = CreateRule(DiscountRuleType.SalePercentage, percentage: 10m);
        var threshold10A = CreateRule(DiscountRuleType.SaleThresholdPercentage, percentage: 10m, thresholdAmount: 50m);
        var threshold10B = CreateRule(DiscountRuleType.SaleThresholdPercentage, percentage: 10m, thresholdAmount: 80m);

        // Make percent10 newest, but threshold10B should still win on "threshold specificity"
        SetCreatedAt(percent10, SaleTime.AddMinutes(3));
        SetCreatedAt(threshold10A, SaleTime.AddMinutes(1));
        SetCreatedAt(threshold10B, SaleTime.AddMinutes(2));

        var calculator = CreateCalculatorWithRules([percent10, threshold10A, threshold10B]);

        var result = await calculator.CalculateAsync(new SalePricingCalculationRequest(
            ShopId,
            SaleTime,
            [line],
            SaleDiscount: None()));

        Assert.False(result.IsError);
        Assert.NotNull(result.Value.ConfiguredSaleRule);
        Assert.Equal(threshold10B.Id, result.Value.ConfiguredSaleRule!.RuleId);
        Assert.Equal(10m, result.Value.TotalDiscountAmount);
    }

    [Fact]
    public async Task Calculate_WhenConfiguredSaleRuleExistsButNoEligibleLines_ReturnsInfoNotError()
    {
        var configuredSale = CreateRule(DiscountRuleType.SalePercentage, percentage: 10m);
        var calculator = CreateCalculatorWithRules([configuredSale]);

        var result = await calculator.CalculateAsync(new SalePricingCalculationRequest(
            ShopId,
            SaleTime,
            [
                Line(quantity: 1m, salesPrice: 50m, taxRatePercent: 0m, taxIncluded: false, costPrice: 50m),
            ],
            SaleDiscount: None()));

        Assert.False(result.IsError);
        Assert.Single(result.Value.Infos, i => i.Code == "sale_pricing.info.no_eligible_lines_for_configured_sale_discount");
        Assert.Equal(0m, result.Value.TotalDiscountAmount);
        Assert.Null(result.Value.ConfiguredSaleRule);
    }

    [Fact]
    public async Task Calculate_CallsDiscountRepositoryWithSaleTime()
    {
        var repo = Substitute.For<IDiscountRuleRepository>();
        repo.GetActiveByShopAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DiscountRule>());

        var calculator = new SalePricingCalculator(repo);

        var result = await calculator.CalculateAsync(new SalePricingCalculationRequest(
            ShopId,
            SaleTime,
            [
                Line(quantity: 1m, salesPrice: 100m, taxRatePercent: 0m, taxIncluded: false, costPrice: 0m),
            ],
            SaleDiscount: None()));

        Assert.False(result.IsError);
        await repo.Received(1).GetActiveByShopAsync(ShopId, SaleTime, Arg.Any<CancellationToken>());
    }

    private static SalePricingCalculator CreateCalculatorWithRules(IReadOnlyList<DiscountRule> rules)
    {
        var repo = Substitute.For<IDiscountRuleRepository>();
        repo.GetActiveByShopAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(rules);
        return new SalePricingCalculator(repo);
    }

    private static DiscountRule CreateRule(
        DiscountRuleType ruleType,
        decimal percentage,
        Guid? inventoryBatchId = null,
        decimal? thresholdAmount = null)
    {
        var created = DiscountRule.Create(
            shopId: ShopId,
            ruleType: ruleType,
            name: $"{ruleType} {percentage}",
            description: null,
            inventoryBatchId: inventoryBatchId,
            percentage: percentage,
            thresholdAmount: thresholdAmount,
            startsAt: null,
            endsAt: null,
            belowCostConfirmed: true,
            belowCostConfirmationReason: null,
            createdBy: Guid.NewGuid());

        Assert.False(created.IsError);
        return created.Value;
    }

    private static void SetCreatedAt(DiscountRule rule, DateTimeOffset createdAt)
    {
        var prop = rule.GetType().BaseType!.GetProperty("CreatedAt");
        prop!.SetValue(rule, createdAt);
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
