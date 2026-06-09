using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Dashboard.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Dashboard.Queries.GetDashboard;

public sealed class GetDashboardQueryHandler(ISaleRepository saleRepository, IExpenseRepository expenseRepository)
{
    private const int MaxRangeDays = 90;
    private const int DefaultRangeDays = 29;

    public async Task<ErrorOr<DashboardDto>> HandleAsync(GetDashboardQuery query, CancellationToken cancellationToken)
    {
        if (!IsAllowedRole(query.Role))
            return Errors.Dashboard.UserIsNotOwnerOrManager;

        var validationResult = ValidateDateRange(query.From, query.To);
        if (validationResult.IsError)
            return validationResult.Errors;

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var appliedTo = query.To ?? today;
        var appliedFrom = query.From ?? appliedTo.AddDays(-DefaultRangeDays);

        var summary = await saleRepository.GetHistorySummaryAsync(query.ShopId, appliedFrom, appliedTo, cancellationToken);
        var latestSales = await saleRepository.GetLatestDashboardSalesAsync(query.ShopId, cancellationToken);
        var expenseTotal = await expenseRepository.GetSumByShopAndDateRangeAsync(query.ShopId, appliedFrom, appliedTo, cancellationToken);

        var result = new DashboardDto(
            GeneratedAt: DateTimeOffset.UtcNow,
            StartDate: appliedFrom,
            EndDate: appliedTo,
            SalesCount: summary.InvoiceCount,
            SalesRevenue: summary.PeriodSales,
            HasNoSalesActivity: summary.InvoiceCount == 0,
            SalesBooked: 0m,
            NetSalesBooked: summary.PeriodSales,
            WastageCost: 0m,
            CashCollected: 0m,
            ProfitBeforeTax: 0m,
            ProfitAfterTax: 0m,
            ExpenseRecorded: 0m,
            ExpenseCorrection: 0m,
            NetExpense: expenseTotal,
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
                CreditSalesPercentage: 0m),
            LatestSales: latestSales.Select(s => new DashboardLatestSaleDto(
                s.SaleId,
                s.InvoiceNumber,
                s.CustomerDisplayName,
                s.SoldAt,
                s.TotalAmount)).ToList());

        return result;
    }

    private static bool IsAllowedRole(string role) =>
        string.Equals(role, "Owner", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase);

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
