using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Exports.Sales.DTOs;

public sealed record SalesExportSummaryRowDto(
    string InvoiceNumber,
    DateTimeOffset SaleDate,
    DateOnly BusinessDate,
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
    int ItemCount,
    decimal CreditNoteAppliedAmount = 0m,
    string? IssuedCreditNoteCodes = null,
    decimal IssuedCreditNoteAmount = 0m,
    IReadOnlyList<InvoiceTaxComponent>? TaxComponents = null)
{
    public string Status { get; init; } = "Posted";
    public string? CancellationReference { get; init; }
    public DateOnly? CancellationBusinessDate { get; init; }
}
