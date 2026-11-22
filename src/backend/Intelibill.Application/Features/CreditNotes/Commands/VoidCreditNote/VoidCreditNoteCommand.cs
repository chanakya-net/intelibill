using Intelibill.Application.Common.Security;
namespace Intelibill.Application.Features.CreditNotes.Commands.VoidCreditNote;

public sealed record VoidCreditNoteCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    string Code,
    string Reason,
    SessionClaims? Session);
