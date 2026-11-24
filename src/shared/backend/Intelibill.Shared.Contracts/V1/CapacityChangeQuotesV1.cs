using System.Text.Json.Serialization;

namespace Intelibill.Shared.Contracts.V1;

/// <summary>
/// Owner preview of a prorated capacity increase on one subscription term. A quote is immutable evidence; it never
/// creates a checkout order, payment attempt or coverage change. Shop pages are bound to the quote, never supplied.
/// </summary>
public static class CapacityChangeQuotesV1
{
    public const string Policy = "capacity-change-quote/1";
    public const string Create = "billing.capacity-change.quote";
    public const string Shops = "billing.capacity-change.shops";
    public const int MaxCreateBytes = 1024;
    public const int MaxQuoteBytes = 8192;
    public const int PageSize = CheckoutOrdersV1.PageSize;
    public const int MaxPageBytes = CheckoutOrdersV1.MaxPageBytes;
    public const string Invalid = "Billing.CapacityChangeInvalid";
    public const string SourceChanged = "Billing.CapacityChangeSourceChanged";
    public const string NeedsReview = "Billing.CapacityChangeNeedsReview";
    public const string NotFound = "Billing.CapacityChangeNotFound";

    public static readonly string[] Outcomes =
        ["Payable", "AtRenewalOrCourtesy", "RenewalRequired", "NewTermRequired", "TrialConversion", "PaymentUnavailable"];

    public static bool IsAction(string action) => action is Create or Shops;

    public static string Path(Guid account, Guid term) =>
        $"{MerchantBillingDelegationV1.AccountsPath}/{account:D}/subscriptions/{term:D}/capacity-change-quotes";

    public static string ShopsPath(Guid account, Guid term, Guid quote, Guid? after) => after is { } cursor
        ? $"{Path(account, term)}/{quote:D}/shops/after/{cursor:D}" : $"{Path(account, term)}/{quote:D}/shops";

    // Canonical ordered JSON array: lower-case D UUIDs and UTC microsecond instants, like checkout quotes.
    public static string CreateHash(Guid term, CreateCapacityChangeQuoteV1 value) => CheckoutOrdersV1.Digest(Policy, "create",
        term, value.TargetPlanVersionId, value.TargetCapacity, value.ObservedCapacity,
        CheckoutOrdersV1.Instant(value.ObservedPaidThrough));

    public static string SnapshotHash(CapacityChangeQuoteV1 q) => CheckoutOrdersV1.Digest(Policy, "snapshot",
        q.AccountId, q.QuoteId, q.TermId, q.Outcome, q.Reason, q.Actions, q.SourcePlanVersionId, q.TargetPlanId,
        q.TargetPlanVersionId, q.TargetPlanVersion, q.TargetPlanName, q.Period, q.PriorCapacity, q.TargetCapacity,
        q.PriorMemberAllowance, q.TargetMemberAllowance, q.ShopCount, q.ShopHash, q.SourceHash, q.PurchasedGrossPaise,
        q.TargetGrossPaise, q.DifferencePaise, q.AmountPaise, q.Currency, q.PriceBasis, CheckoutOrdersV1.Instant(q.IntervalStartsAt),
        CheckoutOrdersV1.Instant(q.PaidThrough), CheckoutOrdersV1.Instant(q.EvaluatedAt), q.TotalSeconds, q.RemainingSeconds,
        q.AnchorLocal, q.Ordinal, q.BillingTimeZone, q.ProrationVersion, q.CalendarVersion, q.SuspensionDisclosed,
        CheckoutOrdersV1.Instant(q.CreatedAt), CheckoutOrdersV1.Instant(q.ExpiresAt), CheckoutOrdersV1.Instant(q.ValidUntil),
        q.DevelopmentFixture);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateCapacityChangeQuoteV1([property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("targetPlanVersionId")] Guid TargetPlanVersionId,
    [property: JsonPropertyName("targetCapacity")] int TargetCapacity,
    [property: JsonPropertyName("observedCapacity")] int ObservedCapacity,
    [property: JsonPropertyName("observedPaidThrough")] DateTimeOffset ObservedPaidThrough)
{
    public bool IsValid() => OperationKey != Guid.Empty && TargetPlanVersionId != Guid.Empty &&
        TargetCapacity is > 0 and <= CheckoutOrdersV1.MaxShops && ObservedCapacity is > 0 and <= CheckoutOrdersV1.MaxShops &&
        TargetCapacity >= ObservedCapacity &&
        EntitlementSnapshotsV1.IsInstant(ObservedPaidThrough.ToUniversalTime());
}

/// <summary>
/// The frozen preview. <c>AmountPaise</c> is present only for <c>Payable</c>; every other outcome names its next step
/// in <c>Actions</c>. A quote evaluated before paid-through expires at that boundary or after 15 minutes, whichever
/// comes first. At or after paid-through, the review lasts 15 minutes. Both cutoffs are end-exclusive.
/// </summary>
public sealed record CapacityChangeQuoteV1(Guid AccountId, Guid QuoteId, Guid TermId, string Outcome, string? Reason,
    string[] Actions, Guid? SourcePlanVersionId, Guid TargetPlanId, Guid TargetPlanVersionId, int TargetPlanVersion,
    string TargetPlanName, string Period, int PriorCapacity, int TargetCapacity, int PriorMemberAllowance,
    int TargetMemberAllowance, int ShopCount, string ShopHash, string SourceHash, long? PurchasedGrossPaise,
    long TargetGrossPaise, long? DifferencePaise, long? AmountPaise, string Currency, string PriceBasis,
    DateTimeOffset IntervalStartsAt, DateTimeOffset PaidThrough, DateTimeOffset EvaluatedAt, long? TotalSeconds,
    long? RemainingSeconds, string? AnchorLocal, int? Ordinal, string BillingTimeZone, string ProrationVersion,
    string CalendarVersion, bool SuspensionDisclosed, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    DateTimeOffset ValidUntil, [property: JsonRequired] bool DevelopmentFixture, string SnapshotHash)
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = 1;
}

public sealed record CapacityChangeShopV1(Guid ShopId, string Name);
public sealed record CapacityChangeShopPageV1(Guid AccountId, Guid QuoteId, CapacityChangeShopV1[] Items, Guid? NextCursor);
