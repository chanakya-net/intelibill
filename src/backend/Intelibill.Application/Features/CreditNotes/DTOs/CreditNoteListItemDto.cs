using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.CreditNotes.DTOs;

public sealed record CreditNoteListItemDto(
    Guid CreditNoteId,
    string Code,
    CreditNoteStatus Status,
    decimal OriginalAmount,
    decimal AvailableBalance,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset IssuedAt,
    Guid SaleReturnId,
    string ReturnNumber,
    Guid SaleId,
    string InvoiceNumber,
    string? CustomerName);
