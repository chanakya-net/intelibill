using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Dashboard.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Dashboard.Queries.GetDashboard;

public sealed class GetDashboardQueryHandler
{
    private const int MaxRangeDays = 90;
    private const int DefaultRangeDays = 29;

    private readonly ISaleRepository _saleRepository;

    public GetDashboardQueryHandler(ISaleRepository saleRepository)
    {
        _saleRepository = saleRepository;
    }

    public async Task<ErrorOr<DashboardDto>> HandleAsync(GetDashboardQuery query, CancellationToken cancellationToken)
    {
        var validationResult = ValidateDateRange(query.From, query.To);
        if (validationResult.IsError)
            return validationResult.Errors;

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var appliedTo = query.To ?? today;
        var appliedFrom = query.From ?? appliedTo.AddDays(-DefaultRangeDays);

        var salesSummary = await _saleRepository.GetHistorySummaryAsync(
            query.ShopId, appliedFrom, appliedTo, cancellationToken);

        var result = new DashboardDto(
            GeneratedAt: DateTimeOffset.UtcNow,
            StartDate: appliedFrom,
            EndDate: appliedTo,
            SalesCount: salesSummary.InvoiceCount,
            SalesRevenue: salesSummary.PeriodSales,
            HasNoSalesActivity: salesSummary.InvoiceCount == 0,
            SalesBooked: 0m,
            NetSalesBooked: 0m,
            WastageCost: 0m,
            CashCollected: 0m,
            ProfitBeforeTax: 0m,
            ProfitAfterTax: 0m,
            ExpenseRecorded: 0m,
            ExpenseCorrection: 0m,
            NetExpense: 0m,
            CreditSalesAmount: 0m,
            CreditSalesPercentage: 0m,
            PaymentMix: new PaymentMixDto(0m, 0m, 0m, 0m),
            CreditShareWarning: false,
            RunningLowStockCount: 0,
            CriticalStockCount: 0,
            RankedShortageList: Array.Empty<StockShortageItemDto>(),
            HighestDueCustomer: new CustomerDueDto(Guid.Empty, string.Empty, 0m),
            TopFiveDueCustomers: Array.Empty<CustomerDueDto>(),
            Alerts: Array.Empty<DashboardAlertDto>(),
            SalesTrendSeries: Array.Empty<SalesTrendPointDto>(),
            ProfitTrendSeries: Array.Empty<ProfitTrendPointDto>(),
            PaymentMixTrendSeries: Array.Empty<PaymentMixTrendPointDto>(),
            PreviousPeriodSummary: new PreviousPeriodSummaryDto(
                StartDate: appliedFrom,
                EndDate: appliedTo,
                SalesCount: 0,
                SalesBooked: 0m,
                NetSalesBooked: 0m,
                ProfitAfterTax: 0m,
                NetExpense: 0m,
                CreditSalesPercentage: 0m));

        return result;
    }

    private static ErrorOr<Success> ValidateDateRange(DateOnly? from, DateOnly? to)
    {
        if (from.HasValue != to.HasValue)
            return Errors.Dashboard.PartialDateRange;

        if (from.HasValue && to.HasValue)
        {
            if (from.Value > to.Value)
                return Errors.Dashboard.InvalidDateRange;

            if (to.Value.DayNumber - from.Value.DayNumber >= MaxRangeDays)
                return Errors.Dashboard.DateRangeTooLarge;
        }

        return Result.Success;
    }
}
