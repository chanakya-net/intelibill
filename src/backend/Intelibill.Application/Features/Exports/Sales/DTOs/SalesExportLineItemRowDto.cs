namespace Intelibill.Application.Features.Exports.Sales.DTOs;

public sealed record SalesExportLineItemRowDto(
    string InvoiceNumber,
    DateTimeOffset SaleDate,
    string? CustomerName,
    string ItemName,
    decimal SalesQuantity,
    decimal SalesPrice,
    decimal ItemDiscountAmount,
    decimal SaleDiscountAmount,
    decimal TaxRatePercent,
    decimal TaxableAmount,
    decimal TaxAmount,
    decimal LineTotal,
    bool IsPriceIncludingTax,
    decimal ReturnedQuantity,
    string? ReturnStatus,
    string? ReturnNumbers);
