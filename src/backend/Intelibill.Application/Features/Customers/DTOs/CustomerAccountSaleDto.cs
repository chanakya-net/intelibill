using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Customers.DTOs;

public sealed record CustomerAccountSaleDto(
    Guid SaleId,
    string InvoiceNumber,
    PaymentMethod PaymentMethod,
    DateTimeOffset SoldAt,
    decimal PaidAmount,
    decimal DueAmount,
    decimal TotalAmount);
