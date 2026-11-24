using System.Text.Json.Serialization;
using ErrorOr;
using Intelibill.Licensing.Application.Status;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Refunds;

public static class RefundApprovalRules
{
    public const string Policy = "refund-approval/1";
    public const string Preserve = "Preserve";
    public const string EndAt = "EndAt";
    public const string TermAdjustment = "TermAdjustment";
    public const string UnrecordedPolicy = "unrecorded";
    public const int MaxCommandBytes = 4096;

    /// <summary>Preserve carries no instant; an effective treatment states exactly one.</summary>
    public static bool HasDates(string? treatment, DateTimeOffset? effectiveAt) =>
        treatment is Preserve ? effectiveAt is null : treatment is EndAt or TermAdjustment && effectiveAt is not null;

    /// <summary>Canonical command identity: amount, normalized reason, explicit treatment and dates, version, digest.</summary>
    public static string PayloadHash(Guid request, ApproveRefund command, string reason) =>
        CheckoutOrdersV1.Digest(Policy, "approve", request, command.AmountPaise, reason, command.Treatment,
            command.EffectiveAt is { } at ? CheckoutOrdersV1.Instant(at) : null, command.ExpectedVersion, command.ImpactDigest);
}

public static class RefundApprovalErrors
{
    public static Error Invalid => Error.Validation("Refund.ApprovalInvalid",
        "Enter a positive whole-paise amount, a reason, one access treatment and confirm the reviewed preview.");
    public static Error RequestMissing => Error.NotFound("Refund.RequestNotFound", "This refund request is unavailable.");
    public static Error OperationMissing => Error.NotFound("Refund.OperationNotFound", "This approval is unavailable.");
    public static Error PayloadMismatch => Error.Conflict("Refund.ApprovalPayloadMismatch", "This approval key was already used.");
    public static Error PreviewStale => Error.Conflict("Refund.PreviewStale",
        "The payment or its access changed after this preview. Review the current preview.");
    public static Error AlreadyDecided => Error.Conflict("Refund.AlreadyDecided", "This refund request already has a decision.");
    public static Error AmountExceeds(long remaining) => Error.Conflict("Refund.AmountExceedsRemaining",
        "The amount exceeds the request or what remains refundable on the verified payment.", new() { ["remainingAmountPaise"] = remaining });
    public static Error TreatmentUnavailable => Error.Conflict("Refund.AccessTreatmentUnavailable",
        "This access treatment cannot apply to the payment's current term at that instant.");
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewRefundApproval(
    [property: JsonPropertyName("amountPaise")] long AmountPaise,
    [property: JsonPropertyName("treatment")] string? Treatment,
    [property: JsonPropertyName("effectiveAt")] DateTimeOffset? EffectiveAt = null)
{
    public override string ToString() => "[REDACTED]";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApproveRefund(
    [property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("amountPaise")] long AmountPaise,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("treatment")] string? Treatment,
    [property: JsonPropertyName("effectiveAt")] DateTimeOffset? EffectiveAt,
    [property: JsonPropertyName("expectedVersion")] long? ExpectedVersion,
    [property: JsonPropertyName("impactDigest")] string? ImpactDigest,
    [property: JsonPropertyName("confirmed")] bool Confirmed)
{
    public override string ToString() => "[REDACTED]";
}

public sealed record RefundAccessShop(Guid ShopId, string ShopName, bool Restricted,
    DateTimeOffset? ScheduledRestrictionAt, bool Closed, DateTimeOffset? CourtesyEndsAt);

/// <summary>The named access effect of one treatment on the term the payment funded; instants are exact UTC.</summary>
public sealed record RefundAccessImpact(string Treatment, Guid? TermId, RefundAccessShop[] Shops, DateTimeOffset? EffectiveAt,
    DateTimeOffset? PreviousPaidThrough, DateTimeOffset? PreviousAccessEndsAt, DateTimeOffset? NewPaidThrough,
    DateTimeOffset? NewAccessEndsAt);

public sealed record RefundApprovalPreview(Guid AccountId, Guid RefundRequestId, Guid OrderId, Guid ConfirmationId,
    string Status, long VerifiedAmountPaise, long RequestedAmountPaise, long OccupiedAmountPaise, long RemainingAmountPaise,
    long ProposedAmountPaise, long ProposedRemainingAmountPaise, string Currency, string PaymentSource, string ProviderId,
    string ProviderAccountId, string Environment, string ConfigurationVersion, string RefundPolicyVersion,
    long ReceiptVersion, string ImpactDigest, RefundAccessImpact Access)
{
    [JsonIgnore] public DateTimeOffset ReviewedAt { get; init; }
    [JsonIgnore] public RefundAccessTarget? Target { get; init; }
    public int SchemaVersion { get; init; } = 1;
    public override string ToString() => "[REDACTED]";
}

/// <summary>The immutable approval receipt: the held amount and its separately recorded access decision.</summary>
public sealed record RefundApprovalReceipt(Guid ApprovalId, Guid ReservationId, Guid AccessDecisionId, Guid AccountId,
    Guid RefundRequestId, Guid OrderId, Guid ConfirmationId, Guid OperationKey, string Status, long ApprovedAmountPaise,
    long RequestedAmountPaise, long VerifiedAmountPaise, long RemainingBeforePaise, long RemainingAfterPaise,
    string Currency, long ReceiptVersion, Guid AdminId, DateTimeOffset MfaVerifiedAt, DateTimeOffset DecidedAt,
    string Reason, RefundAccessImpact Access)
{
    public int SchemaVersion { get; init; } = 1;
    public override string ToString() => "[REDACTED]";
}

public sealed record RefundApprovalOutcome(RefundApprovalReceipt Receipt, bool Created);

/// <summary>The request with its frozen payment pin and accepted refund-policy version.</summary>
public sealed record RefundApprovalSource(Guid AccountId, Guid RefundRequestId, Guid OrderId, Guid ConfirmationId,
    Guid? AttemptId, Guid? ManualReceiptId, long PaymentAmountPaise, long RequestedAmountPaise, string Currency,
    string ProviderId, string ProviderAccountId, string Environment, string ConfigurationVersion,
    string RefundPolicyVersion, bool Decided);

public sealed record RefundReceiptLedger(long Revision, long OccupiedPaise);

/// <summary>The term this payment funded, its current named shops and the commitments that block an effect.</summary>
public sealed record RefundAccessTarget(TermFactsRow Term, RefundTargetShop[] Shops, long TermRevision, bool Committed);

public sealed record RefundTargetShop(Guid ShopId, Guid RequestId, string ShopName, bool Restricted,
    DateTimeOffset? ScheduledRestrictionAt, bool Closed, DateTimeOffset? CourtesyEndsAt);

public sealed record StoredRefundApproval(RefundApprovalReceipt Receipt, string PayloadHash);

public sealed record NewRefundApproval(RefundApprovalSource Source, RefundApprovalReceipt Receipt, Guid SessionId,
    DateTimeOffset AuthorizedAt, string PayloadHash, string ImpactDigest, long? TermRevision);

public interface IRefundApprovalStore
{
    Task<RefundApprovalSource?> SourceAsync(Guid account, Guid request, CancellationToken ct);
    Task LockReceiptAsync(Guid confirmation, CancellationToken ct);
    Task<RefundReceiptLedger> LedgerAsync(Guid account, Guid confirmation, CancellationToken ct);
    Task<RefundAccessTarget?> TargetAsync(RefundApprovalSource source, DateTimeOffset at, CancellationToken ct);
    Task<StoredRefundApproval?> FindAsync(Guid account, Guid operationKey, CancellationToken ct);
    Task SaveAsync(NewRefundApproval approval, CancellationToken ct);
}
