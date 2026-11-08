namespace Intelibill.Application.Features.Sales.DTOs;

public sealed record SalesHistorySummaryDto(
    decimal PeriodSales,
    int InvoiceCount,
    decimal RefundAmount,
    decimal CancellationAmount = 0m,
    int CancellationCount = 0);

