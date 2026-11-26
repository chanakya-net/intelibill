using System.Text.Json.Serialization;

namespace Intelibill.Shared.Contracts.V1;

/// <summary>
/// P06-07a: a proposed move of one named shop between two billing accounts and the three purpose-bound owner
/// consents it needs. A proposal or consent never changes affiliation, coverage, money or access; provider review
/// and application are a separate contract.
/// </summary>
public static class CoverageTransfersV1
{
    public const int SchemaVersion = 1;
    public const string PolicyVersion = "coverage-transfer/1";

    public const string Preview = "billing.coverage-transfer.preview";
    public const string Request = "billing.coverage-transfer.request";
    public const string Recover = "billing.coverage-transfer.recover";
    public const string List = "billing.coverage-transfer.list";
    public const string Read = "billing.coverage-transfer.read";
    public const string Consent = "billing.coverage-transfer.consent";
    public const string ConsentRecover = "billing.coverage-transfer.consent-recover";
    public const string OwnerRead = "billing.coverage-transfer.owner-read";

    public const string SourceBillingOwner = "SourceBillingOwner";
    public const string DestinationBillingOwner = "DestinationBillingOwner";
    public const string ShopOwner = "ShopOwner";
    public const string SourcePurposeSegment = "source-billing-owner";
    public const string DestinationPurposeSegment = "destination-billing-owner";

    public const string Accepted = "Accepted";
    public const string Revoked = "Revoked";
    public const string AwaitingConsent = "AwaitingConsent";
    public const string Stale = "Stale";

    public const string RetainUntilPaidThrough = "RetainUntilPaidThrough";
    public const string ReviewedAllocation = "ReviewedAllocation";

    public const string AwaitingConsents = "AwaitingConsents";
    public const string ConsentsCollected = "ConsentsCollected";
    public const string Expired = "Expired";
    public const string Retired = "Retired";

    public const string ConsentKind = "CoverageTransferConsent";
    public const string ConsentReceiptPath = "/internal/licensing/v1/merchant-events/coverage-transfer-consent";
    public const string ShopAuthorityPath = "/internal/licensing/v1/coverage-transfers/check-shop-authority";
    public const string ProofHeader = "X-Intelibill-Coverage-Transfer-Proof";

    public const string DisconnectedQueuesNotice =
        "Disconnected device queues are unknown. Observed permissions reflect only permits and uploads already reported to the server; existing permits cannot be remotely revoked.";

    public const int PageSize = 20;
    public const int MaxReasonLength = 500;
    public const int MaxRequestBytes = 4096;
    public const int ResponseLimit = 64 * 1024;
    public const int ProposalLifetimeDays = 7;

    public static bool IsAction(string? action) =>
        action is Preview or Request or Recover or List or Read or Consent or ConsentRecover or OwnerRead;

    public static bool IsTreatment(string? value) => value is RetainUntilPaidThrough or ReviewedAllocation;

    public static bool IsDecision(string? value) => value is Accepted or Revoked;

    /// <summary>The billing-owner purpose a route segment names; the shop owner consents on the merchant side.</summary>
    public static string? BillingPurpose(string? segment) => segment switch
    {
        SourcePurposeSegment => SourceBillingOwner,
        DestinationPurposeSegment => DestinationBillingOwner,
        _ => null,
    };

    public static string PurposeSegment(string purpose) =>
        purpose == SourceBillingOwner ? SourcePurposeSegment : DestinationPurposeSegment;

    public static string? Decision(string? segment) => segment switch
    {
        "accept" => Accepted,
        "revoke" => Revoked,
        _ => null,
    };

    public static string? NormalizeReason(string? reason) => CoverageReplacementExceptionsV1.NormalizeReason(reason);

    private static string Account(Guid account) => $"{MerchantBillingDelegationV1.AccountsPath}/{account:D}";

    public static string RequestPath(Guid source, Guid term) =>
        $"{Account(source)}/subscriptions/{term:D}/coverage-transfers";

    public static string PreviewPath(Guid source, Guid term) => RequestPath(source, term) + "/preview";

    public static string RecoverPath(Guid source, Guid term, Guid key) =>
        $"{RequestPath(source, term)}/operations/{key:D}";

    public static string ListPath(Guid account) => $"{Account(account)}/coverage-transfers";

    public static string ReadPath(Guid account, Guid transfer) => $"{ListPath(account)}/{transfer:D}";

    public static string ConsentPath(Guid account, Guid transfer, string purpose, string decision) =>
        $"{ReadPath(account, transfer)}/consents/{PurposeSegment(purpose)}/{(decision == Accepted ? "accept" : "revoke")}";

    public static string ConsentRecoverPath(Guid account, Guid transfer, string purpose, Guid key) =>
        $"{ReadPath(account, transfer)}/consents/{PurposeSegment(purpose)}/operations/{key:D}";

    public static string OwnerPath(Guid source, Guid shop, Guid transfer) =>
        $"{Account(source)}/coverage-transfer-shops/{shop:D}/{transfer:D}/owner";

    /// <summary>Everything every consenting party agrees to. A change to any input is a different proposal.</summary>
    public static string TermsDigest(Guid transferId, long proposalRevision, Guid sourceAccountId, Guid destinationAccountId,
        Guid shopId, Guid sourceAffiliationId, Guid sourceTermId, string treatment, DateTimeOffset? cutoverAt,
        DateTimeOffset sourcePaidThroughAt, string evidenceDigest, string permitDigest, DateTimeOffset expiresAt) =>
        CheckoutOrdersV1.Digest(PolicyVersion, new
        {
            transferId,
            proposalRevision,
            sourceAccountId,
            destinationAccountId,
            shopId,
            sourceAffiliationId,
            sourceTermId,
            treatment,
            cutoverAt,
            sourcePaidThroughAt,
            evidenceDigest,
            permitDigest,
            expiresAt,
        });
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewCoverageTransferV1(
    [property: JsonPropertyName("destinationAccountId")] Guid DestinationAccountId,
    [property: JsonPropertyName("shopId")] Guid ShopId,
    [property: JsonPropertyName("treatment")] string Treatment,
    [property: JsonPropertyName("cutoverAt")] DateTimeOffset? CutoverAt);

/// <summary>What the source Billing Owner reviews before requesting. Destination facts appear only as an opaque digest.</summary>
public sealed record CoverageTransferPreviewV1(
    Guid SourceAccountId, Guid DestinationAccountId, Guid TermId, Guid ShopId, string ShopName, Guid SourceAffiliationId,
    DateTimeOffset SourcePaidThroughAt, long SourceOwnerRevision, string EvidenceDigest,
    string Treatment, DateTimeOffset? CutoverAt, int ObservedPermitCount, DateTimeOffset? ObservedPermitDeadline,
    DateTimeOffset? LastContactAt, DateTimeOffset ObservedAt, bool DisconnectedQueuesUnknown,
    string DisconnectedQueuesNotice, DateTimeOffset ProposedExpiresAt);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequestCoverageTransferV1(
    [property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("destinationAccountId")] Guid DestinationAccountId,
    [property: JsonPropertyName("shopId")] Guid ShopId,
    [property: JsonPropertyName("treatment")] string Treatment,
    [property: JsonPropertyName("cutoverAt")] DateTimeOffset? CutoverAt,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("expectedSourceAffiliationId")] Guid ExpectedSourceAffiliationId,
    [property: JsonPropertyName("expectedSourcePaidThroughAt")] DateTimeOffset ExpectedSourcePaidThroughAt,
    [property: JsonPropertyName("expectedSourceOwnerRevision")] long ExpectedSourceOwnerRevision,
    [property: JsonPropertyName("expectedEvidenceDigest")] string ExpectedEvidenceDigest,
    [property: JsonPropertyName("acknowledgedNamedShop")] bool AcknowledgedNamedShop,
    [property: JsonPropertyName("acknowledgedRemainingAccessTreatment")] bool AcknowledgedRemainingAccessTreatment,
    [property: JsonPropertyName("acknowledgedExistingPermits")] bool AcknowledgedExistingPermits,
    [property: JsonPropertyName("acknowledgedUnknownDisconnectedQueues")] bool AcknowledgedUnknownDisconnectedQueues,
    [property: JsonPropertyName("acknowledgedHistoricalBillsStay")] bool AcknowledgedHistoricalBillsStay)
{
    public bool IsValid() =>
        OperationKey != Guid.Empty && DestinationAccountId != Guid.Empty && ShopId != Guid.Empty &&
        CoverageTransfersV1.IsTreatment(Treatment) && ExpectedSourceAffiliationId != Guid.Empty &&
        EntitlementSnapshotsV1.IsInstant(ExpectedSourcePaidThroughAt) && ExpectedSourceOwnerRevision > 0 &&
        EntitlementSnapshotsV1.IsDigest(ExpectedEvidenceDigest) &&
        (Treatment == CoverageTransfersV1.ReviewedAllocation
            ? CutoverAt is { } cutover && EntitlementSnapshotsV1.IsInstant(cutover) && cutover < ExpectedSourcePaidThroughAt
            : CutoverAt is null) &&
        CoverageTransfersV1.NormalizeReason(Reason) is not null &&
        AcknowledgedNamedShop && AcknowledgedRemainingAccessTreatment && AcknowledgedExistingPermits &&
        AcknowledgedUnknownDisconnectedQueues && AcknowledgedHistoricalBillsStay;
}

/// <summary>One required consent as a participant may see it: the purpose and its current standing, never who holds it elsewhere.</summary>
public sealed record CoverageTransferConsentStatusV1(string Purpose, string Status, int Generation, DateTimeOffset? RecordedAt);

/// <summary>
/// The narrow participant view. It names the shop, both parties and the agreed dates and treatment. It never carries
/// subscriptions, prices, bills, orders, devices or provider notes of either account.
/// <c>ViewerPurposes</c> lists the consents the reader's own live authority could give, with that authority's revision.
/// </summary>
public sealed record CoverageTransferV1(
    Guid TransferId, long ProposalRevision, string TermsDigest,
    Guid SourceAccountId, string SourceAccountName, Guid DestinationAccountId, string? DestinationAccountName,
    Guid ShopId, string ShopName, string Treatment, DateTimeOffset? CutoverAt, DateTimeOffset SourcePaidThroughAt,
    int ObservedPermitCount, DateTimeOffset? ObservedPermitDeadline, bool DisconnectedQueuesUnknown,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, string State, string? StateReason,
    CoverageTransferConsentStatusV1[] Consents, bool ConsentsCollected, bool AwaitingProviderReview,
    string[] ViewerPurposes, long? ViewerAuthorityRevision);

public sealed record CoverageTransferListV1(Guid AccountId, DateTimeOffset EvaluatedAt, CoverageTransferV1[] Items, bool HasMore);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CoverageTransferDecisionV1(
    [property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("expectedProposalRevision")] long ExpectedProposalRevision,
    [property: JsonPropertyName("expectedTermsDigest")] string ExpectedTermsDigest,
    [property: JsonPropertyName("expectedAuthorityRevision")] long ExpectedAuthorityRevision)
{
    public bool IsValid() => OperationKey != Guid.Empty && ExpectedProposalRevision > 0 &&
        EntitlementSnapshotsV1.IsDigest(ExpectedTermsDigest) && ExpectedAuthorityRevision >= 0;
}

/// <summary>What the named shop's owner is shown before deciding: their shop, the parties, the dates and the treatment.</summary>
public sealed record CoverageTransferShopOwnerV1(
    Guid TransferId, long ProposalRevision, string TermsDigest, Guid SourceAccountId, string SourceAccountName,
    Guid DestinationAccountId, string? DestinationAccountName, Guid ShopId, string ShopName,
    string Treatment, DateTimeOffset? CutoverAt, DateTimeOffset SourcePaidThroughAt,
    int ObservedPermitCount, DateTimeOffset? ObservedPermitDeadline, bool DisconnectedQueuesUnknown,
    DateTimeOffset ExpiresAt, string State, string ConsentStatus, string Delivery, long AuthorityRevision);

/// <summary>The private row a shop owner's merchant-side consent is checked against. Never returned to a browser.</summary>
public sealed record CoverageTransferOwnerTermsV1(CoverageTransferV1 Transfer, Guid SourceAffiliationId);

/// <summary>The shop owner's signed decision, delivered from the merchant outbox. <c>Generation</c> orders decisions.</summary>
public sealed record CoverageTransferShopConsentV1(
    int SchemaVersion, Guid EventId, Guid TransferId, Guid AccountId, Guid DestinationAccountId, Guid ShopId,
    Guid AffiliationRequestId, long ProposalRevision, string TermsDigest, Guid ActorId, Guid MembershipId,
    long AuthorityVersion, int Generation, string Kind, DateTimeOffset RecordedAt)
{
    public bool IsValid() =>
        SchemaVersion == CoverageTransfersV1.SchemaVersion && EventId != Guid.Empty && TransferId != Guid.Empty &&
        AccountId != Guid.Empty && DestinationAccountId != Guid.Empty && AccountId != DestinationAccountId &&
        ShopId != Guid.Empty && AffiliationRequestId != Guid.Empty && ProposalRevision > 0 &&
        EntitlementSnapshotsV1.IsDigest(TermsDigest) && ActorId != Guid.Empty && MembershipId != Guid.Empty &&
        AuthorityVersion >= 0 && Generation > 0 && CoverageTransfersV1.IsDecision(Kind) &&
        EntitlementSnapshotsV1.IsInstant(RecordedAt);
}

/// <summary>Licensing's signed question to the merchant service at review time, bound to one invocation by its challenge.</summary>
public sealed record CoverageTransferShopAuthorityCheckV1(
    int SchemaVersion, Guid Challenge, Guid TransferId, Guid AccountId, Guid DestinationAccountId, Guid ShopId,
    Guid AffiliationRequestId, long ProposalRevision, string TermsDigest, Guid ExpectedConsentEventId,
    Guid ExpectedActorId, Guid ExpectedMembershipId, long ExpectedAuthorityVersion, DateTimeOffset Deadline)
{
    public bool IsValid() =>
        SchemaVersion == CoverageTransfersV1.SchemaVersion && Challenge != Guid.Empty && TransferId != Guid.Empty &&
        AccountId != Guid.Empty && DestinationAccountId != Guid.Empty && AccountId != DestinationAccountId && ShopId != Guid.Empty &&
        AffiliationRequestId != Guid.Empty && ProposalRevision > 0 && EntitlementSnapshotsV1.IsDigest(TermsDigest) &&
        ExpectedConsentEventId != Guid.Empty && ExpectedActorId != Guid.Empty && ExpectedMembershipId != Guid.Empty &&
        ExpectedAuthorityVersion >= 0 && EntitlementSnapshotsV1.IsInstant(Deadline);
}

/// <summary>The merchant's signed answer about the named shop at one instant. <c>Current</c> is false for any stale fact.</summary>
public sealed record CoverageTransferShopAuthorityV1(
    int SchemaVersion, Guid Challenge, Guid TransferId, Guid ShopId, Guid ConsentEventId, bool Current, string? Reason,
    int OutstandingPermitCount, DateTimeOffset? MaxPermitValidUntil, DateTimeOffset EvaluatedAt, string PermitDigest)
{
    public bool IsValid() =>
        SchemaVersion == CoverageTransfersV1.SchemaVersion && Challenge != Guid.Empty && TransferId != Guid.Empty &&
        ShopId != Guid.Empty && ConsentEventId != Guid.Empty && Current == (Reason is null) &&
        (Reason is null || !string.IsNullOrWhiteSpace(Reason)) && OutstandingPermitCount >= 0 &&
        (OutstandingPermitCount == 0 ? MaxPermitValidUntil is null :
            MaxPermitValidUntil is { } end && EntitlementSnapshotsV1.IsInstant(end) && end > EvaluatedAt) &&
        EntitlementSnapshotsV1.IsDigest(PermitDigest) && EntitlementSnapshotsV1.IsInstant(EvaluatedAt);
}
