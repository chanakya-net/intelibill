using System.Text.Json.Serialization;

namespace Intelibill.Shared.Contracts.V1;

public static class ShopClosuresV1
{
    public const string FinancialTreatment = "NoFinancialChange";
    public const string ReviewChangedCode = "Closure.ReviewChanged";
    public const string Closed = "Closed";
    public const int MaxTargets = 100;
    public const int BodyLimit = 192 * 1024;
    public const string ShopAction = "billing.closure.shop";
    public const string AccountRead = "billing.closure.account.read";
    public const string Propose = "billing.closure.account.propose";
    public const string Confirm = "billing.closure.account.confirm";
    public const string OwnerProposal = "billing.closure.owner.read";
    public const string OwnerNotice = "billing.closure.owner.notice";
    public static string AccountPath(Guid account) => $"{MerchantBillingDelegationV1.AccountsPath}/{account:D}/closures";
    public static string ConfirmPath(Guid account, Guid proposal) => $"{AccountPath(account)}/{proposal:D}/confirm";
    public static string OwnerPath(Guid shop, Guid account, Guid affiliation) =>
        $"{ShopPath(shop)}/account/{account:D}/{affiliation:D}";
    public static string NoticePath(Guid shop) => $"{ShopPath(shop)}/consent";
    public static bool IsAction(string action) => action is ShopAction or AccountRead or Propose or Confirm or OwnerProposal or OwnerNotice;
    public const string ValidationPath = "/internal/merchant/v1/closures/validate";
    public const string ProofHeader = "X-Closure-Assertion";
    public static string ShopPath(Guid shop) => $"{MerchantBillingDelegationV1.InternalPrefix}/shops/{shop:D}/closure";
    public static bool IsDigest(string? value) => EntitlementSnapshotsV1.IsDigest(value);
}

public sealed record ShopClosureReviewV1(Guid ShopId, string ShopName, Guid AccountId, Guid AffiliationRequestId,
    long ClosureVersion, long OwnerVersion, DateTimeOffset ObservedAt, string WorkDigest, int OpenTills,
    int UnresolvedCases, int ObservedSubmissions, int LinkedSubmissions, int IncompleteIntakes,
    int UnreadableIntakes, int ObservedDevices, ClosureDeviceV1[] Devices,
    bool DisconnectedQueuesUnknown = true, string FinancialTreatment = ShopClosuresV1.FinancialTreatment)
{
    public bool DevicesTruncated => ObservedDevices > Devices.Length;
}

public sealed record ClosureDeviceV1(string Reference, DateTimeOffset LastContactAt, DateTimeOffset? RecordedLeaseEndsAt,
    string PermitEvidence = "Unavailable");

public sealed record ShopClosureConfirmationV1(Guid OperationKey, long ExpectedVersion, long OwnerVersion, string WorkDigest,
    bool ReviewedKnownWork, bool ConsideredDeviceQueues, string FinancialTreatment)
{
    public bool IsValid() => OperationKey != Guid.Empty && ExpectedVersion is 0 or 1 && OwnerVersion >= 0 &&
        ShopClosuresV1.IsDigest(WorkDigest) && ReviewedKnownWork && ConsideredDeviceQueues &&
        FinancialTreatment == ShopClosuresV1.FinancialTreatment;
}

public sealed record ClosureTargetV1(
    [property: JsonPropertyName("shopId")] Guid ShopId,
    [property: JsonPropertyName("affiliationRequestId")] Guid AffiliationRequestId,
    [property: JsonPropertyName("closureVersion")] long ClosureVersion,
    [property: JsonPropertyName("shopName")] string ShopName);

public sealed record ShopClosureRequestV1(
    [property: JsonPropertyName("accountId")] Guid AccountId,
    [property: JsonPropertyName("target")] ClosureTargetV1 Target,
    [property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("workDigest")] string WorkDigest);

public sealed record ClosureValidationV1(Guid AccountId, Guid ProposalId, Guid Challenge, DateTimeOffset ExpiresAt,
    string ScopeDigest, ClosureTargetV1[] Targets, Guid? RequiredActor, bool Recovering);

public sealed record ClosureValidatedV1(Guid AccountId, Guid ProposalId, Guid Challenge, string ScopeDigest,
    ClosureValidatedTargetV1[] Targets);

public sealed record ClosureValidatedTargetV1(Guid ShopId, Guid ConsentId, string WorkDigest);

public sealed record ClosureSavedV1(Guid Id, Guid AccountId, Guid OperationKey, string Scope, DateTimeOffset SavedAt, string FinancialTreatment,
    ClosureTargetV1[] Shops);

public sealed record ProposeAccountClosureV1(
    [property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("financialTreatment")] string FinancialTreatment);
public sealed record ConfirmAccountClosureV1(
    [property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("scopeDigest")] string ScopeDigest,
    [property: JsonPropertyName("confirmed")] bool Confirmed);

public sealed record AccountClosureProposalV1(Guid Id, Guid AccountId, Guid OperationKey, string ScopeDigest, DateTimeOffset ExpiresAt,
    string FinancialTreatment, ClosureProposalShopV1[] Shops);

public sealed record AccountClosureStateV1(AccountClosureProposalV1? Proposal, ClosedBillingAccountV1? Closed);

public sealed record ClosedBillingAccountV1(Guid AccountId, DateTimeOffset SavedAt, string FinancialTreatment,
    ClosedBillingAccountShopV1[] Shops);

public sealed record ClosedBillingAccountShopV1(Guid ShopId, string ShopName);

public sealed record ClosureProposalShopV1(Guid ShopId, string ShopName, bool ConsentRecorded);

public sealed record OwnerClosureProposalV1(Guid Id, Guid AccountId, string ScopeDigest, DateTimeOffset ExpiresAt,
    int ShopCount, ClosureTargetV1 Target, bool ConsentRecorded = false);

public sealed record OwnerClosureNoticeV1(
    [property: JsonPropertyName("accountId")] Guid AccountId,
    [property: JsonPropertyName("affiliationRequestId")] Guid AffiliationRequestId,
    [property: JsonPropertyName("proposalId")] Guid ProposalId,
    [property: JsonPropertyName("scopeDigest")] string ScopeDigest);

public sealed record ProviderClosureV1(Guid Id, string Scope, DateTimeOffset SavedAt, string FinancialTreatment,
    ProviderClosureShopV1[] Shops);

public sealed record ProviderClosureShopV1(Guid ShopId, string ShopName, string Propagation,
    long? SavedShopRevision, long? AppliedShopRevision, DateTimeOffset? AcknowledgedAt);
