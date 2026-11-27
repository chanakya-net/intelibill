using ErrorOr;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.ValueObjects;

public sealed record TillSettlementEvidence(TillSettlementDisposition Disposition, TenderLine? Tender, string Reason)
{
    public ErrorOr<Success> Validate()
    {
        if (!Enum.IsDefined(Disposition) || string.IsNullOrWhiteSpace(Reason) || Reason.Trim().Length > 500)
            return Error.Validation("Till.SettlementEvidenceInvalid", "A settlement disposition and reason are required.");
        if (Disposition == TillSettlementDisposition.NoPhysicalMovement)
            return Tender is null ? Result.Success : Error.Validation("Till.SettlementEvidenceInvalid", "No physical movement cannot contain a tender.");
        return Tender is null ? Error.Validation("Till.TenderRequired", "Physical settlement requires concrete tender evidence.") : Tender.Validate();
    }
}
