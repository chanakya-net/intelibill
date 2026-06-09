using ErrorOr;
using Intelibill.Application.Features.Dashboard.DTOs;

namespace Intelibill.Application.Features.Dashboard.Queries.GetDashboard;

public sealed class GetDashboardQueryHandler
{
#pragma warning disable CA1822 // Skeleton: repository dependencies will be injected in the aggregation phase
    public Task<ErrorOr<DashboardDto>> HandleAsync(GetDashboardQuery query, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var appliedTo = query.To ?? today;
        var appliedFrom = query.From ?? appliedTo.AddDays(-29);

        var result = new DashboardDto(
            GeneratedAt: DateTimeOffset.UtcNow,
            StartDate: appliedFrom,
            EndDate: appliedTo,
            SalesCount: 0,
            HasNoSalesActivity: true,
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

        return Task.FromResult<ErrorOr<DashboardDto>>(result);
    }
#pragma warning restore CA1822
}
