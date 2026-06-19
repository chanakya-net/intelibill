namespace Intelibill.Application.Features.CreditNotes.Queries.GetCreditNoteByCode;

public sealed record GetCreditNoteByCodeQuery(
    Guid UserId,
    Guid ActiveShopId,
    string Code);
