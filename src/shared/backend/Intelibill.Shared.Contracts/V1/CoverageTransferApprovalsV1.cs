using System.Text.Json.Serialization;

namespace Intelibill.Shared.Contracts.V1;

public static class CoverageTransferApprovalsV1
{
    public const string Prepare = "TransferPrepare";
    public const string Consume = "TransferConsume";
    public const string Abort = "TransferAbort";
    public const string Receipt = "TransferReceipt";
    public const string CommandPath = "/internal/licensing/v1/coverage-transfers/apply";
    public const string ReceiptPath = "/internal/licensing/v1/merchant-events/coverage-transfer-receipt";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TransferCommandV1(int SchemaVersion, Guid EventId, string Phase, Guid TransferId,
    Guid SourceAccountId, Guid DestinationAccountId, Guid ShopId, Guid AffiliationId, Guid TermId,
    long ProposalRevision, string TermsDigest, Guid ShopConsent, Guid ShopOwner, Guid MembershipId,
    long AuthorityRevision, string Treatment, DateTimeOffset CutoverAt, DateTimeOffset PaidThroughAt,
    int MemberAllowance, string PermitDigest, DateTimeOffset PermitEvaluatedAt, string? PrepareDigest = null)
{
    public bool IsValid() => SchemaVersion == 1 && EventId != Guid.Empty && TransferId != Guid.Empty &&
        SourceAccountId != Guid.Empty && DestinationAccountId != Guid.Empty && SourceAccountId != DestinationAccountId &&
        ShopId != Guid.Empty && TermId != Guid.Empty && AffiliationId != Guid.Empty && ShopConsent != Guid.Empty &&
        ShopOwner != Guid.Empty && MembershipId != Guid.Empty && AuthorityRevision >= 0 && ProposalRevision > 0 &&
        EntitlementSnapshotsV1.IsDigest(TermsDigest) && EntitlementSnapshotsV1.IsDigest(PermitDigest) &&
        EntitlementSnapshotsV1.IsInstant(CutoverAt) && EntitlementSnapshotsV1.IsInstant(PaidThroughAt) &&
        EntitlementSnapshotsV1.IsInstant(PermitEvaluatedAt) && MemberAllowance > 0 &&
        (Treatment == CoverageTransfersV1.ReviewedAllocation && CutoverAt < PaidThroughAt ||
            Treatment == CoverageTransfersV1.RetainUntilPaidThrough && CutoverAt == PaidThroughAt) &&
        (Phase == CoverageTransferApprovalsV1.Prepare && PrepareDigest is null ||
            Phase is CoverageTransferApprovalsV1.Consume or CoverageTransferApprovalsV1.Abort && EntitlementSnapshotsV1.IsDigest(PrepareDigest));
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TransferReceiptV1(int SchemaVersion, Guid EventId, string Phase, Guid TransferId,
    Guid SourceAccountId, Guid DestinationAccountId, Guid ShopId, string CommandDigest, string Outcome,
    string? Reason, DateTimeOffset EvaluatedAt)
{
    public bool IsValid() => SchemaVersion == 1 && TransferId != Guid.Empty && EventId != Guid.Empty &&
        SourceAccountId != Guid.Empty && DestinationAccountId != Guid.Empty && ShopId != Guid.Empty &&
        SourceAccountId != DestinationAccountId && EntitlementSnapshotsV1.IsDigest(CommandDigest) &&
        EntitlementSnapshotsV1.IsInstant(EvaluatedAt) && (Phase, Outcome) switch
        {
            (CoverageTransferApprovalsV1.Prepare, "Prepared") or (CoverageTransferApprovalsV1.Consume, "Consumed")
                or (CoverageTransferApprovalsV1.Abort, "Aborted") => Reason is null,
            (CoverageTransferApprovalsV1.Prepare or CoverageTransferApprovalsV1.Consume or CoverageTransferApprovalsV1.Abort,
                "Refused") => !string.IsNullOrWhiteSpace(Reason) && Reason.Length <= 100,
            _ => false,
        };
}
