namespace Intelibill.Application.Features.Dashboard.DTOs;

public sealed record PreviousPeriodSummaryDto(
    DateOnly StartDate,
    DateOnly EndDate,
    int SalesCount,
    decimal SalesBooked,
    decimal NetSalesBooked,
    decimal ProfitAfterTax,
    decimal NetExpense,
    decimal CreditSalesPercentage);
