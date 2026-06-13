namespace Intelibill.Application.Features.CreditNotes.Queries.GetCreditNotePrintByCode;

public sealed record GetCreditNotePrintByCodeQuery(
    Guid UserId,
    Guid ActiveShopId,
    string Code);
