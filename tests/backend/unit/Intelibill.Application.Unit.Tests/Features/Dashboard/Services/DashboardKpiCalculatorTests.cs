using Intelibill.Application.Features.Dashboard.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Unit.Tests.Features.Dashboard.Services;

public class DashboardKpiCalculatorTests
{
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

        var mix = DashboardKpiCalculator.CalculatePaymentMix([sale]);

        Assert.Equal(40m, mix.Cash);
        Assert.Equal(60m, mix.Credit);
        Assert.Equal(0m, mix.Upi);
        Assert.Equal(0m, mix.Card);
    }

    [Fact]
    public void BuildAlerts_WhenOwnerAndAllSignalsPresent_ReturnsExpectedPriorityOrder()
    {
        var highestDue = new Application.Features.Dashboard.DTOs.CustomerDueDto(Guid.NewGuid(), "Big Buyer", 1000m);

        var alerts = DashboardKpiCalculator.BuildAlerts(
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

        var trends = DashboardKpiCalculator.BuildTrendSeries([saleOnMiddleDay], [], start, end);

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

        var summary = DashboardKpiCalculator.BuildPreviousPeriodSummary(
            [partialCashSale, upiSale],
            prevSaleReturns: [],
            prevExpenses: [],
            prevStartDate: prevStart,
            prevEndDate: prevEnd);

        Assert.Equal(200m, summary.SalesBooked);
        Assert.Equal(200m, summary.NetSalesBooked);
        Assert.Equal(0.3m, summary.CreditSalesPercentage);
    }
}
