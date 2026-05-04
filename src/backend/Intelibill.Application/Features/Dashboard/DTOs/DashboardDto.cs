namespace Intelibill.Application.Features.Dashboard.DTOs;

public sealed record DashboardDto(
    DateTimeOffset GeneratedAt,
    DateOnly StartDate,
    DateOnly EndDate,
    // Operational KPIs (all roles)
    int SalesCount,
    bool HasNoSalesActivity,
    // Sales and Profit KPIs (null for Staff)
    decimal? SalesBooked,
    decimal? NetSalesBooked,
    decimal? WastageCost,
    decimal? CashCollected,
    decimal? ProfitBeforeTax,
    decimal? ProfitAfterTax,
    // Expense KPIs (null for Staff)
    decimal? ExpenseRecorded,
    decimal? ExpenseCorrection,
    decimal? NetExpense,
    // Payment Behavior (null for Staff)
    decimal? CreditSalesAmount,
    decimal? CreditSalesPercentage,
    PaymentMixDto? PaymentMix,
    bool? CreditShareWarning,
    // Stock Risk — Live Snapshot (all roles)
    int RunningLowStockCount,
    int CriticalStockCount,
    IReadOnlyList<StockShortageItemDto> RankedShortageList,
    // Receivable Risk — Live Snapshot (null for Staff)
    CustomerDueDto? HighestDueCustomer,
    IReadOnlyList<CustomerDueDto>? TopFiveDueCustomers,
    // Alerts ordered by priority (role-filtered)
    IReadOnlyList<DashboardAlertDto> Alerts,
    // Chart series (null for Staff)
    IReadOnlyList<SalesTrendPointDto>? SalesTrendSeries,
    IReadOnlyList<ProfitTrendPointDto>? ProfitTrendSeries,
    IReadOnlyList<PaymentMixTrendPointDto>? PaymentMixTrendSeries,
    // Previous-period comparison (null for Staff)
    PreviousPeriodSummaryDto? PreviousPeriodSummary);
