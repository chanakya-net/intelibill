using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

public enum PaymentRestorationState { Ready, Suspended, Closed, EvidenceUnavailable }

public sealed record PaymentRestorationEvidence(DateTimeOffset At, AccountRestrictionSnapshotV1? Head, bool CanPublish = true)
{
    public PaymentRestorationState State => Head switch
    {
        { Restriction: "Closed" } => PaymentRestorationState.Closed,
        { } head when EntitlementSnapshotsV1.ReadRestrictionError(head) is null && head.Restriction != "Unrestricted" =>
            PaymentRestorationState.Suspended,
        { } head when EntitlementSnapshotsV1.ReadRestrictionError(head) is null && head.UpstreamVerifiedAt <= At && CanPublish =>
            PaymentRestorationState.Ready,
        _ => PaymentRestorationState.EvidenceUnavailable,
    };

    public bool Resolves(PaymentRestorationHold hold) => State == PaymentRestorationState.Ready &&
        At >= hold.HeldAt && (hold.Restriction is "Trading" or "Security"
            ? Head!.ReleasedRevision >= hold.AccountRevision : hold.Restriction is null or "Unrestricted");
}

public sealed record PaymentRestorationHold(Guid? AttemptId, Guid? ManualReceiptId,
    DateTimeOffset EffectiveAt, DateTimeOffset HeldAt, long? AccountRevision, string? Restriction);

public sealed record FreshPaymentTerm(PaymentRestorationEvidence Evidence, string Kind, DateTimeOffset EffectiveAt);
