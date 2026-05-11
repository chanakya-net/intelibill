using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.DTOs;

public sealed record SaleListItemDto(
    Guid SaleId,
    string InvoiceNumber,
    Guid? CustomerId,
    PaymentMethod PaymentMethod,
    DateTimeOffset SoldAt,
    decimal PaidAmount,
    decimal DueAmount,
    decimal TotalBeforeDiscount,
    decimal TotalDiscountAmount,
    decimal TotalAmount,
    decimal TotalTaxAmount,
    string? CustomerName,
    string? CustomerPhone,
    int ItemCount,
    IReadOnlyList<string> ReturnNumbers);
