namespace Intelibill.Application.Features.Exports.Sales.DTOs;

public sealed record SalesExportReturnRowDto(
    string ReturnNumber,
    DateTimeOffset ProcessedAt,
    string InvoiceNumber,
    string? CustomerName,
    decimal TotalRefundAmount,
    decimal TotalTaxableAmount,
    decimal TotalTaxAmount,
    IReadOnlyList<SalesExportReturnTaxBreakupDto> TaxBreakup,
    bool IsVoided = false,
    string? CreditNoteCode = null,
    decimal CreditNoteAmount = 0m,
    decimal CreditNoteRemainingBalance = 0m);
