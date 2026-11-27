using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.CreditNotes.DTOs;

public sealed record CreditNotePrintDto(
    Guid CreditNoteId,
    string Code,
    CreditNoteStatus Status,
    bool IsUsable,
    decimal OriginalAmount,
    decimal AvailableBalance,
    DateTimeOffset IssuedAt,
    DateTimeOffset? ExpiresAt,
    Guid SaleId,
    string InvoiceNumber,
    Guid SaleReturnId,
    string ReturnNumber,
    string CustomerDisplayName,
    string Reason,
    string? VoidReason);
