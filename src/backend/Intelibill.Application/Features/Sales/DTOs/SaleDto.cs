using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.DTOs;

public sealed record SaleDto(
    Guid SaleId,
    string InvoiceNumber,
    PaymentMethod PaymentMethod,
    DateTimeOffset SoldAt,
    decimal TotalAmount,
    decimal TotalTaxAmount,
    IReadOnlyList<SaleItemDto> Items,
    IReadOnlyList<string> Warnings);
