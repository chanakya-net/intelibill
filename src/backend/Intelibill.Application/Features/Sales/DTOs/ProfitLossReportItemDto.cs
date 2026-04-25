namespace Intelibill.Application.Features.Sales.DTOs;

public sealed record ProfitLossReportItemDto(
    Guid SaleId,
    string InvoiceNumber,
    DateTimeOffset SoldAt,
    string? CustomerName,
    decimal TotalCost,
    decimal RevenueBeforeTax,
    decimal RevenueAfterTax,
    decimal ProfitBeforeTax,
    decimal ProfitAfterTax);
