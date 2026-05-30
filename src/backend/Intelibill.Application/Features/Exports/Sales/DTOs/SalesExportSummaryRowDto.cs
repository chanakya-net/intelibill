namespace Intelibill.Application.Features.Exports.Sales.DTOs;

public sealed record SalesExportSummaryRowDto(
    string InvoiceNumber,
    DateTimeOffset SaleDate,
    string? CustomerName,
    string PaymentMethod,
    decimal PaidAmount,
    decimal DueAmount,
    decimal TotalBeforeDiscount,
    decimal DiscountAmount,
    decimal TaxableAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    string? ReturnNumbers,
    decimal ReturnAmount,
    decimal ReturnTaxableAmount,
    decimal ReturnTaxAmount,
    decimal NetSalesAmount,
    bool HasReturns,
    int ItemCount);
