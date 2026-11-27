using System.Text.Json.Serialization;

namespace Intelibill.Shared.Contracts.V1;

public static class CoverageReplacementExceptionsV1
{
    public const int SchemaVersion = 1;
    public const string PolicyVersion = "coverage-replacement-exception/1";
    public const string Read = "billing.coverage-replacement-exception.read";
    public const string Request = "billing.coverage-replacement-exception.request";
    public const string Recover = "billing.coverage-replacement-exception.recover";
    public const string OwnerRead = "billing.coverage-replacement-exception.owner-read";

    public const string ConsentKind = "CoverageReplacementExceptionConsent";
    public const string ReceiptKind = "CoverageReplacementExceptionReceipt";
    public const string PrepareKind = "CoverageExceptionPrepare";
    public const string ConsumeKind = "CoverageExceptionConsume";
    public const string AbortKind = "CoverageExceptionAbort";

    public const string ConsentReceiptPath = "/internal/licensing/v1/merchant-events/coverage-replacement-exception-consent";
    public const string DecisionReceiptPath = "/internal/licensing/v1/merchant-events/coverage-replacement-exception-receipt";
    public const string PreparePath = "/internal/licensing/v1/coverage-replacement-exceptions/prepare";
    public const string ConsumePath = "/internal/licensing/v1/coverage-replacement-exceptions/consume";
    public const string AbortPath = "/internal/licensing/v1/coverage-replacement-exceptions/abort";

    public const string AwaitingRelease = "Coverage.ExceptionAwaitingOutgoingRelease";
    public const string WaitDisposition = "Wait";
    public const string CourtesyBridgeDisposition = "CourtesyBridge";
    public const string OutgoingLeg = "Outgoing";
    public const string IncomingLeg = "Incoming";
    public const string IncomingPaidLeg = "IncomingPaid";
    public const string IncomingCourtesyLeg = "IncomingCourtesy";

    public const string DisconnectedQueuesNotice =
        "Disconnected device queues are unknown. Observed permissions reflect only uploads and permits already reported to the server; existing permits cannot be remotely revoked.";

    public const int PageSize = 50;
    public const int MaxReasonLength = 500;
    public const int MaxRequestBytes = 4096;

    public static bool IsAction(string? action) => action is Read or Request or Recover or OwnerRead;

    public static bool IsDisposition(string? value) => value is WaitDisposition or CourtesyBridgeDisposition;

    public static bool IsLeg(string? value) => value is OutgoingLeg or IncomingLeg;

    public static bool IsSnapshotLeg(string? value) => value is OutgoingLeg or IncomingPaidLeg or IncomingCourtesyLeg;

    public static string? NormalizeReason(string? reason)
    {
        var trimmed = reason?.Trim();
        return trimmed is { Length: > 0 and <= MaxReasonLength }
            && !trimmed.Any(char.IsControl)
            ? trimmed : null;
    }

    public static string Path(Guid account, Guid term) =>
        $"{MerchantBillingDelegationV1.AccountsPath}/{account:D}/subscriptions/{term:D}/coverage-replacement-exceptions";

    public static string ReadPath(Guid account, Guid term, int page) => page == 1
        ? Path(account, term)
        : FormattableString.Invariant($"{Path(account, term)}/pages/{page}");

    public static string RecoverPath(Guid account, Guid term, Guid key) =>
        $"{Path(account, term)}/operations/{key:D}";

    public static string OwnerPath(Guid account, Guid shop, Guid exceptionId) =>
        $"{MerchantBillingDelegationV1.AccountsPath}/{account:D}/coverage-replacement-exceptions/{shop:D}/{exceptionId:D}/owner";

    public static string TermsDigest(Guid accountId, Guid termId, long proposalRevision, Guid outgoingShopId,
        Guid outgoingAffiliationId, Guid incomingShopId, Guid incomingAffiliationId, int capacity, int memberAllowance,
        DateTimeOffset requestedEarlyStartAt, DateTimeOffset paidCutoverAt, DateTimeOffset normalRenewalAt,
        string overlapDisposition, DateTimeOffset? observedPermitDeadline) =>
        CheckoutOrdersV1.Digest(PolicyVersion, new
        {
            accountId,
            termId,
            proposalRevision,
            outgoingShopId,
            outgoingAffiliationId,
            incomingShopId,
            incomingAffiliationId,
            capacity,
            memberAllowance,
            requestedEarlyStartAt,
            paidCutoverAt,
            normalRenewalAt,
            overlapDisposition,
            observedPermitDeadline,
        });
}

public sealed record EarlyReplacementCoveredShopV1(
    Guid ShopId, string ShopName, DateTimeOffset? ObservedPermitDeadline,
    DateTimeOffset? LastContactAt, DateTimeOffset? LastSynchronizedAt, int OutstandingPermitCount);

public sealed record CoverageReplacementExceptionPreviewV1(
    Guid TermId, int Capacity, int MemberAllowance, DateTimeOffset NormalRenewalAt, DateTimeOffset ObservedAt,
    bool CanRequest, string DisconnectedQueuesNotice, EarlyReplacementCoveredShopV1[] CoveredShops,
    SlotShopV1[] EligibleShops, CoverageReplacementExceptionV1[] Exceptions, bool HasMore);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequestCoverageReplacementExceptionV1(
    [property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("outgoingShopId")] Guid OutgoingShopId,
    [property: JsonPropertyName("incomingShopId")] Guid IncomingShopId,
    [property: JsonPropertyName("requestedEarlyStartAt")] DateTimeOffset RequestedEarlyStartAt,
    [property: JsonPropertyName("proposedPaidCutoverAt")] DateTimeOffset ProposedPaidCutoverAt,
    [property: JsonPropertyName("overlapDisposition")] string OverlapDisposition,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("observedCapacity")] int ObservedCapacity,
    [property: JsonPropertyName("observedMemberAllowance")] int ObservedMemberAllowance,
    [property: JsonPropertyName("observedNormalRenewalAt")] DateTimeOffset ObservedNormalRenewalAt,
    [property: JsonPropertyName("observedPermitDeadline")] DateTimeOffset? ObservedPermitDeadline,
    [property: JsonPropertyName("acknowledgedShopSelection")] bool AcknowledgedShopSelection,
    [property: JsonPropertyName("acknowledgedRemainingAccessAllocation")] bool AcknowledgedRemainingAccessAllocation,
    [property: JsonPropertyName("acknowledgedExistingPermits")] bool AcknowledgedExistingPermits,
    [property: JsonPropertyName("acknowledgedUnknownDisconnectedQueues")] bool AcknowledgedUnknownDisconnectedQueues);

public sealed record CoverageReplacementExceptionV1(
    Guid ExceptionId, Guid AccountId, Guid TermId, long ProposalRevision, string TermsDigest,
    Guid OutgoingShopId, string OutgoingShopName, Guid OutgoingAffiliationId,
    Guid IncomingShopId, string IncomingShopName, Guid IncomingAffiliationId,
    int Capacity, int MemberAllowance,
    DateTimeOffset NormalRenewalAt, DateTimeOffset RequestedEarlyStartAt, DateTimeOffset PaidCutoverAt,
    string OverlapDisposition, DateTimeOffset? ObservedPermitDeadline, DateTimeOffset ObservedAt,
    DateTimeOffset? LastContactAt, DateTimeOffset? LastSynchronizedAt, int ObservedPermitCount,
    bool DisconnectedQueuesUnknown, string RequestReason,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, string State, string? Reason,
    string OutgoingConsentState, string IncomingConsentState,
    Guid? CourtesyGrantId, DateTimeOffset? CourtesyStartsAt, DateTimeOffset? CourtesyEndsAt,
    string OutgoingActivation, string IncomingActivation, string CourtesyActivation,
    DateTimeOffset? ApprovedAt, DateTimeOffset? AppliedAt);

/// <summary>Narrow per-shop owner view: discloses only this owner's leg and required commercial dates, never provider notes or prices.</summary>
public sealed record CoverageReplacementExceptionOwnerV1(
    Guid ExceptionId, Guid AccountId, Guid TermId, long ProposalRevision, string TermsDigest,
    string Leg, Guid ShopId, string ShopName, string CounterpartyShopName,
    DateTimeOffset NormalRenewalAt, DateTimeOffset RequestedEarlyStartAt, DateTimeOffset PaidCutoverAt,
    string OverlapDisposition, int MemberAllowance, DateTimeOffset? ObservedPermitDeadline,
    bool DisconnectedQueuesUnknown, string State, string ConsentStatus, string LegActivation, DateTimeOffset ExpiresAt);

public sealed record CoverageReplacementExceptionConsentV1(
    int SchemaVersion, Guid EventId, Guid ExceptionId, Guid AccountId, Guid TermId,
    long ProposalRevision, string TermsDigest, string Leg, Guid ShopId, Guid AffiliationRequestId,
    Guid ActorId, Guid MembershipId, long AuthorityVersion,
    int ActiveMembers, string SeatHash, int MemberAllowance,
    DateTimeOffset RequestedEarlyStartAt, DateTimeOffset PaidCutoverAt, DateTimeOffset NormalRenewalAt,
    string OverlapDisposition, DateTimeOffset RecordedAt, string Kind)
{
    public bool IsValid() =>
        SchemaVersion == CoverageReplacementExceptionsV1.SchemaVersion && EventId != Guid.Empty &&
        ExceptionId != Guid.Empty && AccountId != Guid.Empty && TermId != Guid.Empty && ProposalRevision > 0 &&
        EntitlementSnapshotsV1.IsDigest(TermsDigest) && CoverageReplacementExceptionsV1.IsLeg(Leg) &&
        ShopId != Guid.Empty && AffiliationRequestId != Guid.Empty && ActorId != Guid.Empty &&
        MembershipId != Guid.Empty && AuthorityVersion >= 0 && ActiveMembers >= 1 && MemberAllowance >= 1 &&
        EntitlementSnapshotsV1.IsDigest(SeatHash) && EntitlementSnapshotsV1.IsInstant(RequestedEarlyStartAt) &&
        EntitlementSnapshotsV1.IsInstant(PaidCutoverAt) && EntitlementSnapshotsV1.IsInstant(NormalRenewalAt) &&
        RequestedEarlyStartAt <= PaidCutoverAt && PaidCutoverAt < NormalRenewalAt &&
        CoverageReplacementExceptionsV1.IsDisposition(OverlapDisposition) &&
        EntitlementSnapshotsV1.IsInstant(RecordedAt) && Kind is "Accepted" or "Revoked";
}

public sealed record CoverageExceptionCommandV1(
    int SchemaVersion, Guid EventId, string Phase, Guid ExceptionId, Guid ApprovalId, long Generation,
    Guid AccountId, Guid TermId, Guid OutgoingShopId, Guid OutgoingAffiliationId,
    Guid IncomingShopId, Guid IncomingAffiliationId, long ProposalRevision, string TermsDigest,
    DateTimeOffset RequestedEarlyStartAt, DateTimeOffset PaidCutoverAt, DateTimeOffset NormalRenewalAt,
    string OverlapDisposition, int MemberAllowance,
    Guid ExpectedOutgoingConsentEvent, Guid ExpectedIncomingConsentEvent,
    DateTimeOffset? ExpectedObservedPermitDeadline, DateTimeOffset ExpiresAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PrepareDigest = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExpectedMerchantPermitDigest = null)
{
    public bool IsValid(string expectedPhase) =>
        SchemaVersion == CoverageReplacementExceptionsV1.SchemaVersion && Phase == expectedPhase &&
        EventId != Guid.Empty && ExceptionId != Guid.Empty && ApprovalId != Guid.Empty && Generation > 0 &&
        AccountId != Guid.Empty && TermId != Guid.Empty && OutgoingShopId != Guid.Empty &&
        OutgoingAffiliationId != Guid.Empty && IncomingShopId != Guid.Empty && IncomingAffiliationId != Guid.Empty &&
        OutgoingShopId != IncomingShopId && ProposalRevision > 0 && EntitlementSnapshotsV1.IsDigest(TermsDigest) &&
        EntitlementSnapshotsV1.IsInstant(RequestedEarlyStartAt) && EntitlementSnapshotsV1.IsInstant(PaidCutoverAt) &&
        EntitlementSnapshotsV1.IsInstant(NormalRenewalAt) && RequestedEarlyStartAt <= PaidCutoverAt &&
        PaidCutoverAt < NormalRenewalAt && CoverageReplacementExceptionsV1.IsDisposition(OverlapDisposition) &&
        (OverlapDisposition != CoverageReplacementExceptionsV1.CourtesyBridgeDisposition || RequestedEarlyStartAt < PaidCutoverAt) &&
        MemberAllowance > 0 && ExpectedOutgoingConsentEvent != Guid.Empty && ExpectedIncomingConsentEvent != Guid.Empty &&
        (ExpectedObservedPermitDeadline is null || EntitlementSnapshotsV1.IsInstant(ExpectedObservedPermitDeadline.Value)) &&
        EntitlementSnapshotsV1.IsInstant(ExpiresAt) &&
        (expectedPhase != CoverageReplacementExceptionsV1.ConsumeKind ||
            (EntitlementSnapshotsV1.IsDigest(PrepareDigest) && EntitlementSnapshotsV1.IsDigest(ExpectedMerchantPermitDigest)));
}

public sealed record CoverageExceptionReceiptV1(
    int SchemaVersion, Guid EventId, string Phase, Guid ExceptionId, Guid ApprovalId, long Generation,
    Guid AccountId, Guid TermId, Guid RoutingShopId, string CommandDigest,
    string Outcome, string? Reason, int OutgoingPermitCount, DateTimeOffset? MaxPermitValidUntil,
    string PermitEvidenceDigest, DateTimeOffset EvaluatedAt)
{
    public bool IsValid()
    {
        var validOutcome = Outcome switch
        {
            "Prepared" => Phase == CoverageReplacementExceptionsV1.PrepareKind && Reason is null,
            "Consumed" => Phase == CoverageReplacementExceptionsV1.ConsumeKind && Reason is null,
            "Aborted" => Phase == CoverageReplacementExceptionsV1.AbortKind && Reason is null,
            "Refused" => Phase is CoverageReplacementExceptionsV1.PrepareKind or CoverageReplacementExceptionsV1.ConsumeKind
                or CoverageReplacementExceptionsV1.AbortKind && Reason is { Length: > 0 and <= 80 },
            _ => false,
        };
        return validOutcome && SchemaVersion == CoverageReplacementExceptionsV1.SchemaVersion &&
            EventId != Guid.Empty && ExceptionId != Guid.Empty && ApprovalId != Guid.Empty && Generation > 0 &&
            AccountId != Guid.Empty && TermId != Guid.Empty && RoutingShopId != Guid.Empty &&
            EntitlementSnapshotsV1.IsDigest(CommandDigest) && OutgoingPermitCount >= 0 &&
            (MaxPermitValidUntil is null || EntitlementSnapshotsV1.IsInstant(MaxPermitValidUntil.Value)) &&
            EntitlementSnapshotsV1.IsDigest(PermitEvidenceDigest) && EntitlementSnapshotsV1.IsInstant(EvaluatedAt);
    }
}
