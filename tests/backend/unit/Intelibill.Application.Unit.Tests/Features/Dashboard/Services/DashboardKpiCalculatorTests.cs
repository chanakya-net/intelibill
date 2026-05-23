using Intelibill.Application.Features.Dashboard.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Unit.Tests.Features.Dashboard.Services;

public class DashboardKpiCalculatorTests
{
    [Fact]
    public void CalculateSalesKpis_WhenAdjustmentLossesExist_IncludesOnlyActiveDecreaseLosses()
    {
        var shopId = Guid.NewGuid();
        var sale = Sale.Create(
            shopId,
            "INV-ADJ",
            null,
            null,
            null,
            PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            paidAmount: 100m,
            dueAmount: 0m,
            totalAmount: 100m,
            totalTaxAmount: 10m,
            [SaleItem.Create(shopId, Guid.NewGuid(), Guid.NewGuid(), 1m, 60m, 100m, 110m, 10m, false, false)]);
        var activeDecrease = MakeAdjustment(shopId, InventoryAdjustmentDirection.Decrease, InventoryAdjustmentReason.Damaged, 25m);
        var increase = MakeAdjustment(shopId, InventoryAdjustmentDirection.Increase, InventoryAdjustmentReason.FoundStock, 15m);
        var voidedDecrease = MakeAdjustment(shopId, InventoryAdjustmentDirection.Decrease, InventoryAdjustmentReason.Expired, 10m);
        voidedDecrease.Void(DateTimeOffset.UtcNow, Guid.NewGuid(), "Mistake", Guid.NewGuid());

        var kpis = SalesKpiCalculator.CalculateSalesKpis(
            [sale],
            saleReturns: [],
            adjustmentLosses: [activeDecrease, increase, voidedDecrease]);

        Assert.Equal(25m, kpis.WastageCost);
        Assert.Equal(15m, kpis.ProfitBeforeTax);
        Assert.Equal(5m, kpis.ProfitAfterTax);
    }

    [Fact]
    public void CalculatePaymentMix_WhenCashSaleHasDue_AllocatesDueToCredit()
    {
        var shopId = Guid.NewGuid();
        var sale = Sale.Create(
            shopId,
            "INV-1",
            null,
            null,
            null,
            PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            paidAmount: 40m,
            dueAmount: 60m,
            totalAmount: 100m,
            totalTaxAmount: 10m,
            [SaleItem.Create(shopId, Guid.NewGuid(), Guid.NewGuid(), 1m, 70m, 100m, 110m, 10m, false, false)]);

        var mix = SalesKpiCalculator.CalculatePaymentMix([sale]);

        Assert.Equal(40m, mix.Cash);
        Assert.Equal(60m, mix.Credit);
        Assert.Equal(0m, mix.Upi);
        Assert.Equal(0m, mix.Card);
    }

    [Fact]
    public void BuildAlerts_WhenOwnerAndAllSignalsPresent_ReturnsExpectedPriorityOrder()
    {
        var highestDue = new Application.Features.Dashboard.DTOs.CustomerDueDto(Guid.NewGuid(), "Big Buyer", 1000m);

        var alerts = DashboardAlertBuilder.BuildAlerts(
            isStaff: false,
            criticalStockCount: 2,
            runningLowStockCount: 3,
            highestDueCustomer: highestDue,
            creditSalesPercentage: 0.5m);

        Assert.Equal(4, alerts.Count);
        Assert.Equal("CriticalStock", alerts[0].AlertType);
        Assert.Equal("HighestDue", alerts[1].AlertType);
        Assert.Equal("RunningLowStock", alerts[2].AlertType);
        Assert.Equal("CreditShareWarning", alerts[3].AlertType);
    }

    [Fact]
    public void BuildTrendSeries_WhenRangeHasGapDays_FillsEmptyBucketsWithZeros()
    {
        var shopId = Guid.NewGuid();
        var start = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-2));
        var end = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var saleOnMiddleDay = Sale.Create(
            shopId,
            "INV-2",
            null,
            null,
            null,
            PaymentMethod.UPI,
            new DateTimeOffset(start.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            paidAmount: 100m,
            dueAmount: 0m,
            totalAmount: 100m,
            totalTaxAmount: 10m,
            [SaleItem.Create(shopId, Guid.NewGuid(), Guid.NewGuid(), 1m, 70m, 100m, 110m, 10m, false, false)]);

        var trends = DashboardTrendBuilder.BuildTrendSeries([saleOnMiddleDay], [], start, end);

        Assert.Equal(3, trends.SalesTrend.Count);
        Assert.Equal(0m, trends.SalesTrend[0].Amount);
        Assert.Equal(100m, trends.SalesTrend[1].Amount);
        Assert.Equal(0m, trends.SalesTrend[2].Amount);

        Assert.Equal(3, trends.PaymentMixTrend.Count);
        Assert.Equal(0m, trends.PaymentMixTrend[0].Upi);
        Assert.Equal(100m, trends.PaymentMixTrend[1].Upi);
        Assert.Equal(0m, trends.PaymentMixTrend[2].Upi);
    }

    [Fact]
    public void BuildTrendSeries_WhenAdjustmentLossesExist_SubtractsDailyActiveDecreaseLosses()
    {
        var shopId = Guid.NewGuid();
        var start = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1));
        var end = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var saleToday = Sale.Create(
            shopId,
            "INV-ADJ-TREND",
            null,
            null,
            null,
            PaymentMethod.Cash,
            new DateTimeOffset(end.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            paidAmount: 100m,
            dueAmount: 0m,
            totalAmount: 100m,
            totalTaxAmount: 10m,
            [SaleItem.Create(shopId, Guid.NewGuid(), Guid.NewGuid(), 1m, 60m, 100m, 110m, 10m, false, false)]);
        var yesterdayLoss = MakeAdjustment(
            shopId,
            InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Damaged,
            30m,
            new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        var todayLoss = MakeAdjustment(
            shopId,
            InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Expired,
            25m,
            new DateTimeOffset(end.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        var ignoredIncrease = MakeAdjustment(
            shopId,
            InventoryAdjustmentDirection.Increase,
            InventoryAdjustmentReason.FoundStock,
            15m,
            new DateTimeOffset(end.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        var ignoredVoided = MakeAdjustment(
            shopId,
            InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Stolen,
            10m,
            new DateTimeOffset(end.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        ignoredVoided.Void(DateTimeOffset.UtcNow, Guid.NewGuid(), "Mistake", Guid.NewGuid());

        var trends = DashboardTrendBuilder.BuildTrendSeries(
            [saleToday],
            saleReturns: [],
            startDate: start,
            endDate: end,
            adjustmentLosses: [yesterdayLoss, todayLoss, ignoredIncrease, ignoredVoided]);

        Assert.Equal(-30m, trends.ProfitTrend[0].ProfitBeforeTax);
        Assert.Equal(-30m, trends.ProfitTrend[0].ProfitAfterTax);
        Assert.Equal(15m, trends.ProfitTrend[1].ProfitBeforeTax);
        Assert.Equal(5m, trends.ProfitTrend[1].ProfitAfterTax);
    }

    [Fact]
    public void BuildPreviousPeriodSummary_WhenDueExists_UsesDueInCreditSalesPercentage()
    {
        var shopId = Guid.NewGuid();
        var prevStart = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-14));
        var prevEnd = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-8));

        var partialCashSale = Sale.Create(
            shopId,
            "INV-3",
            null,
            null,
            null,
            PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            paidAmount: 40m,
            dueAmount: 60m,
            totalAmount: 100m,
            totalTaxAmount: 10m,
            [SaleItem.Create(shopId, Guid.NewGuid(), Guid.NewGuid(), 1m, 70m, 100m, 110m, 10m, false, false)]);

        var upiSale = Sale.Create(
            shopId,
            "INV-4",
            null,
            null,
            null,
            PaymentMethod.UPI,
            DateTimeOffset.UtcNow,
            paidAmount: 100m,
            dueAmount: 0m,
            totalAmount: 100m,
            totalTaxAmount: 10m,
            [SaleItem.Create(shopId, Guid.NewGuid(), Guid.NewGuid(), 1m, 60m, 100m, 110m, 10m, false, false)]);

        var summary = DashboardTrendBuilder.BuildPreviousPeriodSummary(
            [partialCashSale, upiSale],
            prevSaleReturns: [],
            prevExpenses: [],
            prevStartDate: prevStart,
            prevEndDate: prevEnd);

        Assert.Equal(200m, summary.SalesBooked);
        Assert.Equal(200m, summary.NetSalesBooked);
        Assert.Equal(0.3m, summary.CreditSalesPercentage);
    }

    [Fact]
    public void BuildPreviousPeriodSummary_WhenAdjustmentLossesExist_SubtractsActiveDecreaseLossesFromProfit()
    {
        var shopId = Guid.NewGuid();
        var prevStart = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-14));
        var prevEnd = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-8));
        var sale = Sale.Create(
            shopId,
            "INV-PREV-ADJ",
            null,
            null,
            null,
            PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            paidAmount: 100m,
            dueAmount: 0m,
            totalAmount: 100m,
            totalTaxAmount: 10m,
            [SaleItem.Create(shopId, Guid.NewGuid(), Guid.NewGuid(), 1m, 60m, 100m, 110m, 10m, false, false)]);

        var summary = DashboardTrendBuilder.BuildPreviousPeriodSummary(
            [sale],
            prevSaleReturns: [],
            prevExpenses: [],
            prevStartDate: prevStart,
            prevEndDate: prevEnd,
            prevAdjustmentLosses: [MakeAdjustment(shopId, InventoryAdjustmentDirection.Decrease, InventoryAdjustmentReason.Damaged, 25m)]);

        Assert.Equal(5m, summary.ProfitAfterTax);
    }

    private static InventoryAdjustment MakeAdjustment(
        Guid shopId,
        InventoryAdjustmentDirection direction,
        InventoryAdjustmentReason reason,
        decimal costImpact,
        DateTimeOffset? performedAt = null)
    {
        var quantityBefore = 10m;
        var quantity = 1m;
        var quantityAfter = direction == InventoryAdjustmentDirection.Increase
            ? quantityBefore + quantity
            : quantityBefore - quantity;

        return InventoryAdjustment.Create(
            shopId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            $"ADJ-{Guid.NewGuid():N}",
            direction,
            reason,
            quantity,
            unitCost: costImpact,
            costImpact,
            quantityBefore,
            quantityAfter,
            quantityBefore,
            quantityAfter,
            performedAt ?? DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            notes: null,
            Guid.NewGuid()).Value;
    }
}
