using Intelibill.Application.Features.Dashboard.DTOs;
using Intelibill.Application.Features.Dashboard.Queries.GetDashboard;

namespace Intelibill.Application.Unit.Tests.Features.Dashboard.Queries.GetDashboard;

public sealed class GetDashboardQueryHandlerTests
{
    private readonly GetDashboardQueryHandler _handler = new();

    [Fact]
    public async Task HandleAsync_WithNoDateParams_ReturnsDefaultAppliedRangeAndEmptyShape()
    {
        var query = new GetDashboardQuery(Guid.NewGuid(), Guid.NewGuid(), null, null);
        var before = DateTimeOffset.UtcNow;

        var result = await _handler.HandleAsync(query, CancellationToken.None);

        var after = DateTimeOffset.UtcNow;
        Assert.False(result.IsError);
        Assert.InRange(result.Value.GeneratedAt, before, after);
        Assert.Equal(result.Value.EndDate.AddDays(-29), result.Value.StartDate);
        AssertSkeletonShape(result.Value);
    }

    [Fact]
    public async Task HandleAsync_WithExplicitDateRange_UsesProvidedRange()
    {
        var from = new DateOnly(2026, 5, 1);
        var to = new DateOnly(2026, 5, 31);
        var query = new GetDashboardQuery(Guid.NewGuid(), Guid.NewGuid(), from, to);

        var result = await _handler.HandleAsync(query, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(from, result.Value.StartDate);
        Assert.Equal(to, result.Value.EndDate);
        AssertSkeletonShape(result.Value);
    }

    private static void AssertSkeletonShape(DashboardDto dto)
    {
        Assert.Equal(0, dto.SalesCount);
        Assert.True(dto.HasNoSalesActivity);
        Assert.Equal(0m, dto.SalesBooked);
        Assert.Equal(0m, dto.NetSalesBooked);
        Assert.Equal(0m, dto.WastageCost);
        Assert.Equal(0m, dto.CashCollected);
        Assert.Equal(0m, dto.ProfitBeforeTax);
        Assert.Equal(0m, dto.ProfitAfterTax);
        Assert.Equal(0m, dto.ExpenseRecorded);
        Assert.Equal(0m, dto.ExpenseCorrection);
        Assert.Equal(0m, dto.NetExpense);
        Assert.Equal(0m, dto.CreditSalesAmount);
        Assert.Equal(0m, dto.CreditSalesPercentage);
        Assert.NotNull(dto.PaymentMix);
        Assert.Equal(0m, dto.PaymentMix!.Cash);
        Assert.Equal(0m, dto.PaymentMix.Upi);
        Assert.Equal(0m, dto.PaymentMix.Card);
        Assert.Equal(0m, dto.PaymentMix.Credit);
        Assert.False(dto.CreditShareWarning);
        Assert.Equal(0, dto.RunningLowStockCount);
        Assert.Equal(0, dto.CriticalStockCount);
        Assert.Empty(dto.RankedShortageList);
        Assert.NotNull(dto.HighestDueCustomer);
        Assert.Equal(Guid.Empty, dto.HighestDueCustomer!.CustomerId);
        Assert.Equal(string.Empty, dto.HighestDueCustomer.DisplayName);
        Assert.Equal(0m, dto.HighestDueCustomer.OutstandingDue);
        Assert.NotNull(dto.TopFiveDueCustomers);
        Assert.Empty(dto.TopFiveDueCustomers);
        Assert.Empty(dto.Alerts);
        Assert.NotNull(dto.SalesTrendSeries);
        Assert.Empty(dto.SalesTrendSeries);
        Assert.NotNull(dto.ProfitTrendSeries);
        Assert.Empty(dto.ProfitTrendSeries);
        Assert.NotNull(dto.PaymentMixTrendSeries);
        Assert.Empty(dto.PaymentMixTrendSeries);
        Assert.NotNull(dto.PreviousPeriodSummary);
        Assert.Equal(dto.StartDate, dto.PreviousPeriodSummary!.StartDate);
        Assert.Equal(dto.EndDate, dto.PreviousPeriodSummary.EndDate);
        Assert.Equal(0, dto.PreviousPeriodSummary.SalesCount);
    }
}
