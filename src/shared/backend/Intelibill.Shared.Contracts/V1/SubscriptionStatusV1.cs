using System.Text.Json.Serialization;

namespace Intelibill.Shared.Contracts.V1;

/// <summary>
/// Versioned, observe-only subscription status shared by merchant and provider. One evaluation per response at
/// <c>evaluatedAt</c>; every interval is end-exclusive UTC with its billing zone. Commercial facts are omitted
/// server-side unless the caller holds commercial authority. Synchronization reports signed merchant confirmation.
/// Payment verification reads <see cref="NotAvailable"/>.
/// </summary>
public static class SubscriptionStatusV1
{
    public const int SchemaVersion = 1;
    public const string ShopRead = "licensing.shop-status.read";
    public const string AccountRead = "licensing.account-status.read";
    public const string NotAvailable = "NotAvailable";
    public const int PageSize = 50;
    public const int MaximumCoveredShops = 100;
    public const int ShopResponseLimit = 8 * 1024;
    // Up to 5,000 shop rows with 250 UTF-16 units (up to six escaped bytes each), sync facts and term overhead.
    public const int AccountResponseLimit = 10 * 1024 * 1024;

    public static readonly string[] States =
        ["PendingActivation", "Scheduled", "Active", "Grace", "Expired", "Suspended", "EvidenceIncomplete", "NeedsReview"];
    public static readonly string[] Bases = ["Paid", "Trial", "Complimentary", "Temporary", "Transition"];
    public static readonly string[] EvidenceStates = ["Complete", "Incomplete", "NeedsReview"];

    public static readonly string[] SynchronizationStates = ["Pending", "Applied", "NeedsReview"];
    public static readonly string[] DeliveryStates = ["Queued", "Retrying", "AwaitingAcknowledgement", "Acknowledged", "DeadLetter"];
    public static readonly string[] DeliveryFailures =
        ["Unavailable", "Rejected", "InvalidResponse", "AcknowledgementMissing", "LeaseExpired", "DeliveryUnconfirmed"];
    public static readonly string[] ReviewDispositions =
        ["AffiliationUnresolved", "MissingScope", "InvalidAccountScope", "InvalidShopScope",
         "ConflictingAccountScope", "ConflictingShopScope", "InvalidAcknowledgement"];

    public static string Summarize(IEnumerable<CoveredShopV1> shops)
    {
        var state = NotAvailable;
        foreach (var shop in shops)
        {
            if (shop.Synchronization?.State == "NeedsReview") return "NeedsReview";
            if (shop.Synchronization?.State == "Pending") state = "Pending";
            else if (shop.Synchronization?.State == "Applied" && state == NotAvailable) state = "Applied";
        }
        return state;
    }

    public static string ShopPath(Guid shop) => $"{MerchantBillingDelegationV1.InternalPrefix}/shops/{shop:D}/status";

    // The cursor is a position, never authority; the whole canonical path is signed.
    public static string AccountPath(Guid account, Guid? after) => after is { } term
        ? $"{MerchantBillingDelegationV1.AccountsPath}/{account:D}/subscriptions/after/{term:D}"
        : $"{MerchantBillingDelegationV1.AccountsPath}/{account:D}/subscriptions";
}

public sealed record SubscriptionCommercialV1(
    [property: JsonPropertyName("pricePaise")] long? PricePaise,
    [property: JsonPropertyName("currency")] string? Currency,
    [property: JsonPropertyName("planVersionId")] Guid? PlanVersionId);

public sealed record SubscriptionTermStatusV1(
    [property: JsonPropertyName("termId")] Guid TermId,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("evidenceState")] string EvidenceState,
    [property: JsonPropertyName("basis")] string? Basis,
    [property: JsonPropertyName("billingPeriod")] string? BillingPeriod,
    [property: JsonPropertyName("timeZone")] string TimeZone,
    [property: JsonPropertyName("startsAt")] DateTimeOffset StartsAt,
    [property: JsonPropertyName("paidThrough")] DateTimeOffset PaidThrough,
    [property: JsonPropertyName("graceEndsAt")] DateTimeOffset? GraceEndsAt,
    [property: JsonPropertyName("accessEndsAt")] DateTimeOffset? AccessEndsAt,
    [property: JsonPropertyName("nextDeadline")] DateTimeOffset? NextDeadline,
    [property: JsonPropertyName("nextPeriodEndsAt")] DateTimeOffset? NextPeriodEndsAt,
    [property: JsonPropertyName("coverageMode")] string CoverageMode,
    [property: JsonPropertyName("purchasedCapacity")] int PurchasedCapacity,
    [property: JsonPropertyName("memberAllowance")] int? MemberAllowance,
    [property: JsonPropertyName("sourceReference")] string? SourceReference,
    [property: JsonPropertyName("developmentFixture")] bool DevelopmentFixture,
    [property: JsonPropertyName("commercial")] SubscriptionCommercialV1? Commercial,
    [property: JsonPropertyName("cancellation"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    SubscriptionCancellationSummaryV1? Cancellation = null);

public sealed record ShopSubscriptionStatusV1(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("shopId")] Guid ShopId,
    [property: JsonPropertyName("evaluatedAt")] DateTimeOffset EvaluatedAt,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("term")] SubscriptionTermStatusV1? Term,
    [property: JsonPropertyName("accountId")] Guid? AccountId,
    [property: JsonPropertyName("observeOnly")] bool ObserveOnly,
    [property: JsonPropertyName("entitlementSynchronization")] string EntitlementSynchronization,
    [property: JsonPropertyName("paymentVerification")] string PaymentVerification,
    [property: JsonPropertyName("synchronization")] EntitlementSynchronizationV1? Synchronization = null,
    [property: JsonPropertyName("suspension"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CustomerSuspensionV1? Suspension = null,
    [property: JsonPropertyName("enforcedSuspensionMode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? EnforcedSuspensionMode = null,
    [property: JsonPropertyName("closedAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? ClosedAt = null,
    [property: JsonPropertyName("closureEnforced"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? ClosureEnforced = null,
    [property: JsonPropertyName("reopenedAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? ReopenedAt = null,
    [property: JsonPropertyName("reopeningEnforced"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? ReopeningEnforced = null,
    [property: JsonPropertyName("reopeningRequiredRevision"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    long? ReopeningRequiredRevision = null);

public sealed record EntitlementSynchronizationV1(
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("delivery")] string Delivery,
    [property: JsonPropertyName("savedAccountRevision")] long SavedAccountRevision,
    [property: JsonPropertyName("savedShopRevision")] long SavedShopRevision,
    [property: JsonPropertyName("savedVerifiedAt")] DateTimeOffset SavedVerifiedAt,
    [property: JsonPropertyName("appliedAccountRevision")] long? AppliedAccountRevision,
    [property: JsonPropertyName("appliedShopRevision")] long? AppliedShopRevision,
    [property: JsonPropertyName("lastSynchronizedAt")] DateTimeOffset? LastSynchronizedAt,
    [property: JsonPropertyName("attention")] string? Attention);

public sealed record CoveredShopV1(
    [property: JsonPropertyName("shopId")] Guid ShopId,
    [property: JsonPropertyName("shopName")] string ShopName,
    [property: JsonPropertyName("synchronization")] EntitlementSynchronizationV1? Synchronization = null);

public sealed record AccountTermStatusV1(
    [property: JsonPropertyName("status")] SubscriptionTermStatusV1 Status,
    [property: JsonPropertyName("coveredShops")] CoveredShopV1[] CoveredShops,
    [property: JsonPropertyName("coveredShopCount")] int CoveredShopCount);

public sealed record AccountSubscriptionStatusV1(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("accountId")] Guid AccountId,
    [property: JsonPropertyName("evaluatedAt")] DateTimeOffset EvaluatedAt,
    [property: JsonPropertyName("items")] AccountTermStatusV1[] Items,
    [property: JsonPropertyName("nextCursor")] Guid? NextCursor,
    [property: JsonPropertyName("observeOnly")] bool ObserveOnly,
    [property: JsonPropertyName("entitlementSynchronization")] string EntitlementSynchronization,
    [property: JsonPropertyName("paymentVerification")] string PaymentVerification);
