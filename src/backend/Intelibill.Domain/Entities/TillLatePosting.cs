using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Entities;

public sealed class TillLatePosting : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid OfflineSubmissionId { get; private set; }
    public Guid SaleId { get; private set; }
    public Guid CaseId { get; private set; }
    public Guid? SessionId { get; private set; }
    public Guid? CloseId { get; private set; }
    public DateOnly SourceBusinessDate { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public Guid CashierId { get; private set; }
    public IReadOnlyList<TenderLine> TenderLines { get; private set; } = [];
    public string EvidenceStatus { get; private set; } = string.Empty;
    public string ManagerReason { get; private set; } = string.Empty;
    public string? EvidenceReason { get; private set; }
    public bool IncludedInCount { get; private set; }
    private TillLatePosting() { }
    public void AttachEvidenceReason(string reason)
    {
        if (EvidenceReason is not null || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            throw new InvalidOperationException("Supplemental evidence requires one immutable reason.");
        EvidenceReason = reason.Trim();
    }
    public static ErrorOr<TillLatePosting> Record(Guid shopId, Guid submissionId, Guid saleId, Guid caseId, Guid? sessionId,
        Guid? closeId, DateOnly businessDate, DateTimeOffset occurredAt, DateTimeOffset recordedAt, Guid cashierId,
        IReadOnlyList<TenderLine> tenderLines, string evidenceStatus, string managerReason)
    {
        if (shopId == Guid.Empty || submissionId == Guid.Empty || saleId == Guid.Empty || caseId == Guid.Empty || cashierId == Guid.Empty ||
            sessionId == Guid.Empty || closeId == Guid.Empty || (closeId.HasValue && !sessionId.HasValue) || occurredAt > recordedAt ||
            string.IsNullOrWhiteSpace(managerReason) || managerReason.Trim().Length > 500 ||
            evidenceStatus is not ("Unknown" or "Recorded") || tenderLines.Count > 1 ||
            (evidenceStatus == "Unknown" && tenderLines.Count != 0))
            return Error.Validation("Till.LatePostingInvalid", "A supplemental posting requires valid references, evidence and a manager reason.");
        foreach (var line in tenderLines)
        {
            var valid = line.Validate();
            if (valid.IsError) return valid.Errors;
        }
        return new TillLatePosting { ShopId = shopId, OfflineSubmissionId = submissionId, SaleId = saleId, CaseId = caseId,
            SessionId = sessionId, CloseId = closeId, SourceBusinessDate = businessDate, OccurredAt = occurredAt, RecordedAt = recordedAt,
            CashierId = cashierId, TenderLines = Array.AsReadOnly(tenderLines.ToArray()), EvidenceStatus = evidenceStatus, ManagerReason = managerReason.Trim() };
    }
}
