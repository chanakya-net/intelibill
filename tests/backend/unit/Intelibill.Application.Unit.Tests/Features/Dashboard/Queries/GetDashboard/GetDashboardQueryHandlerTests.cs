using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Dashboard.DTOs;
using Intelibill.Application.Features.Dashboard.Queries.GetDashboard;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Dashboard.Queries.GetDashboard;

public sealed class GetDashboardQueryHandlerTests
{
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();
    private readonly GetDashboardQueryHandler _handler;

    public GetDashboardQueryHandlerTests()
    {
        _saleRepository
            .GetHistorySummaryAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new SalesHistorySummaryReadModel(PeriodSales: 0m, InvoiceCount: 0, RefundAmount: 0m));

        _handler = new GetDashboardQueryHandler(_saleRepository);
    }

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

    [Fact]
    public async Task HandleAsync_WithOnlyFromDate_ReturnsPartialDateRangeError()
    {
        var query = new GetDashboardQuery(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 5, 1), null);

        var result = await _handler.HandleAsync(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Single(result.Errors);
        Assert.Equal(Errors.Dashboard.PartialDateRange.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WithOnlyToDate_ReturnsPartialDateRangeError()
    {
        var query = new GetDashboardQuery(Guid.NewGuid(), Guid.NewGuid(), null, new DateOnly(2026, 5, 31));

        var result = await _handler.HandleAsync(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Single(result.Errors);
        Assert.Equal(Errors.Dashboard.PartialDateRange.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WithFromGreaterThanTo_ReturnsInvalidDateRangeError()
    {
        var query = new GetDashboardQuery(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 5, 31), new DateOnly(2026, 5, 1));

        var result = await _handler.HandleAsync(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Single(result.Errors);
        Assert.Equal(Errors.Dashboard.InvalidDateRange.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WithRangeOver90Days_ReturnsDateRangeTooLargeError()
    {
        var from = new DateOnly(2026, 1, 1);
        var to = from.AddDays(91);
        var query = new GetDashboardQuery(Guid.NewGuid(), Guid.NewGuid(), from, to);

        var result = await _handler.HandleAsync(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Single(result.Errors);
        Assert.Equal(Errors.Dashboard.DateRangeTooLarge.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WithRangeExactly90Days_Succeeds()
    {
        var from = new DateOnly(2026, 1, 1);
        var to = from.AddDays(89);
        var query = new GetDashboardQuery(Guid.NewGuid(), Guid.NewGuid(), from, to);

        var result = await _handler.HandleAsync(query, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(from, result.Value.StartDate);
        Assert.Equal(to, result.Value.EndDate);
    }

    [Fact]
    public async Task HandleAsync_WithSalesData_PopulatesSalesRevenueAndInvoiceCount()
    {
        var shopId = Guid.NewGuid();
        var from = new DateOnly(2026, 5, 1);
        var to = new DateOnly(2026, 5, 31);
        var query = new GetDashboardQuery(Guid.NewGuid(), shopId, from, to);

        _saleRepository
            .GetHistorySummaryAsync(shopId, from, to, Arg.Any<CancellationToken>())
            .Returns(new SalesHistorySummaryReadModel(PeriodSales: 9500m, InvoiceCount: 42, RefundAmount: 500m));

        var result = await _handler.HandleAsync(query, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(9500m, result.Value.SalesRevenue);
        Assert.Equal(42, result.Value.SalesCount);
        Assert.False(result.Value.HasNoSalesActivity);
    }

    [Fact]
    public async Task HandleAsync_WithNoSales_SalesRevenueIsZeroAndHasNoSalesActivity()
    {
        var query = new GetDashboardQuery(Guid.NewGuid(), Guid.NewGuid(), null, null);

        var result = await _handler.HandleAsync(query, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(0m, result.Value.SalesRevenue);
        Assert.Equal(0, result.Value.SalesCount);
        Assert.True(result.Value.HasNoSalesActivity);
    }

    [Fact]
    public async Task HandleAsync_SalesRevenue_EqualsGrossSalesMinusReturns()
    {
        var shopId = Guid.NewGuid();
        var from = new DateOnly(2026, 6, 1);
        var to = new DateOnly(2026, 6, 30);
        var query = new GetDashboardQuery(Guid.NewGuid(), shopId, from, to);

        _saleRepository
            .GetHistorySummaryAsync(shopId, from, to, Arg.Any<CancellationToken>())
            .Returns(new SalesHistorySummaryReadModel(PeriodSales: 4800m, InvoiceCount: 10, RefundAmount: 200m));

        var result = await _handler.HandleAsync(query, CancellationToken.None);

        Assert.False(result.IsError);
        // PeriodSales = gross - returns = 5000 - 200 = 4800
        Assert.Equal(4800m, result.Value.SalesRevenue);
        Assert.Equal(10, result.Value.SalesCount);
    }

    private static void AssertSkeletonShape(DashboardDto dto)
    {
        Assert.Equal(0, dto.SalesCount);
        Assert.Equal(0m, dto.SalesRevenue);
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
