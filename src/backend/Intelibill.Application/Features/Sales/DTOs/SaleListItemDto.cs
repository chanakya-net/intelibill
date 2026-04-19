using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.DTOs;

public sealed record SaleListItemDto(
    Guid SaleId,
    string InvoiceNumber,
    PaymentMethod PaymentMethod,
    DateTimeOffset SoldAt,
    decimal TotalAmount,
    decimal TotalTaxAmount,
    string? CustomerName,
    string? CustomerPhone,
    int ItemCount);
