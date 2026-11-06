using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Customers.DTOs;

/// <param name="DueAmount">The due printed when the invoice was issued; it never changes.</param>
/// <param name="CurrentOutstandingAmount">What is still owed on the invoice after allocated payments and returns.</param>

public sealed record CustomerAccountSaleDto(
    Guid SaleId,
    string InvoiceNumber,
    PaymentMethod PaymentMethod,
    DateTimeOffset SoldAt,
    decimal PaidAmount,
    decimal DueAmount,
    decimal TotalAmount,
    decimal AllocatedPaymentAmount = 0m,
    decimal ReturnCreditAmount = 0m,
    decimal CurrentOutstandingAmount = 0m)
{
    /// <summary>When the due falls due, fixed at issue from agreed terms; null = no terms (outstanding, never overdue).</summary>
    public DateOnly? DueDate { get; init; }

    /// <summary>True only when something is still owed and the due date has passed.</summary>
    public bool IsOverdue { get; init; }
}
