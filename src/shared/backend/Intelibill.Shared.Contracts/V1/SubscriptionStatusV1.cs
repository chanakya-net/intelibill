using System.Text.Json.Serialization;

namespace Intelibill.Shared.Contracts.V1;

/// <summary>
/// Versioned, observe-only subscription status shared by merchant and provider. One evaluation per response at
/// <c>evaluatedAt</c>; every interval is end-exclusive UTC with its billing zone. Commercial facts are omitted
/// server-side unless the caller holds commercial authority. Synchronization and payment verification are not
/// implemented in this version and always read <see cref="NotAvailable"/>.
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
    public const int AccountResponseLimit = 2 * 1024 * 1024;

    public static readonly string[] States =
        ["PendingActivation", "Scheduled", "Active", "Grace", "Expired", "Suspended", "EvidenceIncomplete", "NeedsReview"];
    public static readonly string[] Bases = ["Paid", "Trial", "Complimentary", "Temporary", "Transition"];
    public static readonly string[] EvidenceStates = ["Complete", "Incomplete", "NeedsReview"];

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
    [property: JsonPropertyName("commercial")] SubscriptionCommercialV1? Commercial);

public sealed record ShopSubscriptionStatusV1(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("shopId")] Guid ShopId,
    [property: JsonPropertyName("evaluatedAt")] DateTimeOffset EvaluatedAt,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("term")] SubscriptionTermStatusV1? Term,
    [property: JsonPropertyName("accountId")] Guid? AccountId,
    [property: JsonPropertyName("observeOnly")] bool ObserveOnly,
    [property: JsonPropertyName("entitlementSynchronization")] string EntitlementSynchronization,
    [property: JsonPropertyName("paymentVerification")] string PaymentVerification);

public sealed record CoveredShopV1(
    [property: JsonPropertyName("shopId")] Guid ShopId,
    [property: JsonPropertyName("shopName")] string ShopName);

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
