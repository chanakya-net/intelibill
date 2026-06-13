using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.CreditNotes.DTOs;

public sealed record CreditNoteDto(
    Guid CreditNoteId,
    string Code,
    CreditNoteStatus Status,
    decimal OriginalAmount,
    decimal AvailableBalance,
    DateTimeOffset? ExpiresAt,
    bool IsVoided,
    Guid SaleReturnId,
    string Reason,
    string? VoidReason);
