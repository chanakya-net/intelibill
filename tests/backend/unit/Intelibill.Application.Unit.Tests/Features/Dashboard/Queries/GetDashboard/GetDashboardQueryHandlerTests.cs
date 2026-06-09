using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Dashboard.DTOs;
using Intelibill.Application.Features.Dashboard.Queries.GetDashboard;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Dashboard.Queries.GetDashboard;

public sealed class GetDashboardQueryHandlerTests
{
    private static readonly SalesHistorySummaryReadModel DefaultSummary = new(0m, 0, 0m);

    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();
    private readonly ICustomerLedgerEntryRepository _customerLedgerEntryRepository = Substitute.For<ICustomerLedgerEntryRepository>();
    private readonly GetDashboardQueryHandler _handler;

    public GetDashboardQueryHandlerTests()
    {
        _handler = new GetDashboardQueryHandler(_saleRepository, _customerLedgerEntryRepository);
        ConfigureSummary(DefaultSummary);
        ConfigureLatestSales([]);
        ConfigureCustomerCreditDue(0m);
    }

    [Fact]
    public async Task HandleAsync_WithNoDateParams_ReturnsDefaultAppliedRangeAndEmptyShape()
    {
        var query = new GetDashboardQuery(Guid.NewGuid(), Guid.NewGuid(), null, null, "Owner");
        var before = DateTimeOffset.UtcNow;

        var result = await _handler.HandleAsync(query, CancellationToken.None);

        var after = DateTimeOffset.UtcNow;
        Assert.False(result.IsError);
        Assert.InRange(result.Value.GeneratedAt, before, after);
        Assert.Equal(result.Value.EndDate.AddDays(-29), result.Value.StartDate);
        AssertSkeletonShape(result.Value);
        await _saleRepository.Received(1).GetHistorySummaryAsync(
            query.ShopId,
            result.Value.StartDate,
            result.Value.EndDate,
            Arg.Any<CancellationToken>());
        await _saleRepository.Received(1).GetLatestDashboardSalesAsync(query.ShopId, Arg.Any<CancellationToken>());
        await _customerLedgerEntryRepository.Received(1).GetCustomerCreditDueAsync(query.ShopId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithExplicitDateRange_UsesProvidedRange()
    {
        var from = new DateOnly(2026, 5, 1);
        var to = new DateOnly(2026, 5, 31);
        var query = new GetDashboardQuery(Guid.NewGuid(), Guid.NewGuid(), from, to, "Manager");

        var result = await _handler.HandleAsync(query, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(from, result.Value.StartDate);
        Assert.Equal(to, result.Value.EndDate);
        AssertSkeletonShape(result.Value);
        await _saleRepository.Received(1).GetHistorySummaryAsync(
            query.ShopId,
            from,
            to,
            Arg.Any<CancellationToken>());
        await _saleRepository.Received(1).GetLatestDashboardSalesAsync(query.ShopId, Arg.Any<CancellationToken>());
        await _customerLedgerEntryRepository.Received(1).GetCustomerCreditDueAsync(query.ShopId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithSummaryData_PopulatesInvoiceCountAndRevenue()
    {
        var summary = new SalesHistorySummaryReadModel(412.75m, 18, 12.25m);
        ConfigureSummary(summary);
        var query = new GetDashboardQuery(Guid.NewGuid(), Guid.NewGuid(), null, null, "Owner");

        var result = await _handler.HandleAsync(query, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(18, result.Value.SalesCount);
        Assert.False(result.Value.HasNoSalesActivity);
        Assert.Equal(0m, result.Value.CustomerCreditDue);
        Assert.Equal(412.75m, result.Value.NetSalesBooked);
        Assert.Equal(0m, result.Value.SalesBooked);
    }

    [Fact]
    public async Task HandleAsync_WithCustomerCreditDue_PopulatesKpi()
    {
        ConfigureCustomerCreditDue(1250m);
        var query = new GetDashboardQuery(Guid.NewGuid(), Guid.NewGuid(), null, null, "Owner");

        var result = await _handler.HandleAsync(query, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(1250m, result.Value.CustomerCreditDue);
    }

    [Fact]
    public async Task HandleAsync_IncludesLatestSales_RegardlessOfDashboardRange()
    {
        var from = new DateOnly(2026, 5, 1);
        var to = new DateOnly(2026, 5, 31);
        var query = new GetDashboardQuery(Guid.NewGuid(), Guid.NewGuid(), from, to, "Owner");
        var latestSales = new[]
        {
            new DashboardLatestSaleReadModel(Guid.NewGuid(), "INV-003", "Walk-in Customer", new DateTimeOffset(2026, 5, 31, 15, 45, 0, TimeSpan.Zero), 980m),
            new DashboardLatestSaleReadModel(Guid.NewGuid(), "INV-002", "Arun Kumar", new DateTimeOffset(2026, 5, 30, 11, 15, 0, TimeSpan.Zero), 1200m),
        };
        ConfigureLatestSales(latestSales);

        var result = await _handler.HandleAsync(query, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(
            latestSales.Select(s => new DashboardLatestSaleDto(s.SaleId, s.InvoiceNumber, s.CustomerDisplayName, s.SoldAt, s.TotalAmount)),
            result.Value.LatestSales);
        await _saleRepository.Received(1).GetLatestDashboardSalesAsync(query.ShopId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithStaffRole_ReturnsForbidden()
    {
        var query = new GetDashboardQuery(Guid.NewGuid(), Guid.NewGuid(), null, null, "Staff");

        var result = await _handler.HandleAsync(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Single(result.Errors);
        Assert.Equal(Errors.Dashboard.UserIsNotOwnerOrManager.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WithOnlyFromDate_ReturnsPartialDateRangeError()
    {
        var query = new GetDashboardQuery(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 5, 1), null, "Owner");

        var result = await _handler.HandleAsync(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Single(result.Errors);
        Assert.Equal(Errors.Dashboard.PartialDateRange.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WithOnlyToDate_ReturnsPartialDateRangeError()
    {
        var query = new GetDashboardQuery(Guid.NewGuid(), Guid.NewGuid(), null, new DateOnly(2026, 5, 31), "Owner");

        var result = await _handler.HandleAsync(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Single(result.Errors);
        Assert.Equal(Errors.Dashboard.PartialDateRange.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WithFromGreaterThanTo_ReturnsInvalidDateRangeError()
    {
        var query = new GetDashboardQuery(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 5, 31), new DateOnly(2026, 5, 1), "Owner");

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
        var query = new GetDashboardQuery(Guid.NewGuid(), Guid.NewGuid(), from, to, "Owner");

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
        var query = new GetDashboardQuery(Guid.NewGuid(), Guid.NewGuid(), from, to, "Manager");

        var result = await _handler.HandleAsync(query, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(from, result.Value.StartDate);
        Assert.Equal(to, result.Value.EndDate);
    }

    private void ConfigureSummary(SalesHistorySummaryReadModel summary)
    {
        _saleRepository.GetHistorySummaryAsync(
                Arg.Any<Guid>(),
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(summary));
    }

    private void ConfigureLatestSales(IReadOnlyList<DashboardLatestSaleReadModel> latestSales)
    {
        _saleRepository.GetLatestDashboardSalesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(latestSales));
    }

    private void ConfigureCustomerCreditDue(decimal customerCreditDue)
    {
        _customerLedgerEntryRepository.GetCustomerCreditDueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(customerCreditDue));
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
        Assert.Equal(0m, dto.CustomerCreditDue);
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
        Assert.NotNull(dto.LatestSales);
        Assert.Empty(dto.LatestSales);
    }
}
