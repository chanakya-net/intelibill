using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.CreditNotes.Queries.GetCreditNotes;

public sealed record GetCreditNotesQuery(
    Guid UserId,
    Guid ShopId,
    string? SearchTerm,
    CreditNoteStatus? Status,
    int PageNumber = 1,
    int PageSize = 20);
