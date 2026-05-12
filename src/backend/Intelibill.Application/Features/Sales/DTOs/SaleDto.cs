using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.DTOs;

public sealed record SaleDto(
    Guid SaleId,
    string InvoiceNumber,
    Guid? CustomerId,
    string? CustomerName,
    string? CustomerPhone,
    PaymentMethod PaymentMethod,
    DateTimeOffset SoldAt,
    decimal PaidAmount,
    decimal DueAmount,
    decimal TotalBeforeDiscount,
    decimal TotalDiscountAmount,
    decimal TotalAmount,
    decimal TotalTaxAmount,
    IReadOnlyList<SaleItemDto> Items,
    IReadOnlyList<string> Warnings)
{
    public IReadOnlyList<SaleReturnDto> Returns { get; init; } = [];
}
