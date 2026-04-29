namespace Intelibill.Application.Features.Dashboard.DTOs;

public sealed record DashboardDto(
    DateTimeOffset GeneratedAt,
    DateOnly ReportingDay,
    // Sales and Profit KPIs
    int SalesCount,
    decimal SalesBooked,
    decimal CashCollected,
    decimal ProfitBeforeTax,
    decimal ProfitAfterTax,
    // Expense KPIs
    decimal ExpenseRecorded,
    decimal ExpenseCorrection,
    decimal NetExpense,
    // Payment Behavior
    decimal CreditSalesAmount,
    decimal CreditSalesPercentage,
    PaymentMixDto PaymentMix,
    bool CreditShareWarning,
    // Stock Risk (Live Snapshot)
    int RunningLowStockCount,
    int CriticalStockCount,
    IReadOnlyList<StockShortageItemDto> RankedShortageList,
    // Receivable Risk (Live Snapshot)
    CustomerDueDto? HighestDueCustomer,
    IReadOnlyList<CustomerDueDto> TopFiveDueCustomers);

