namespace Intelibill.Application.Features.Sales.DTOs;

public sealed record SalesHistorySummaryDto(
    decimal PeriodSales,
    int InvoiceCount,
    decimal RefundAmount);

