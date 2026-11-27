using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Intelibill.Shared.Contracts.V1;

public static class CheckoutOrdersV1
{
    public const string Policy = "checkout-quote/1";
    public const string FreshStartPolicy = "paid-fresh-start/1";
    public const string Create = "billing.checkout.create";
    public const string Read = "billing.checkout.read";
    public const string Accept = "billing.checkout.accept";
    public const string Recover = "billing.checkout.recover";
    public const string Candidates = "billing.checkout.candidates";
    public const string Shops = "billing.checkout.shops";
    public const int MaxShops = 100_000;
    public const long MaxPaise = 1_000_000_000_000;
    public const int MaxCreateBytes = MaxShops * 40 + 512;
    public const int MaxQuoteBytes = 4096;
    public const int MaxAcceptBytes = 2048;
    public const int PageSize = 40;
    public const int MaxShopName = SubscriptionCancellationsV1.MaximumShopNameLength;
    public const int MaxPageBytes = PageSize * (MaxShopName * 6 + 120) + 512;
    public const string Invalid = "Billing.CheckoutInvalid";
    public const string SourceChanged = "Billing.CheckoutSourceChanged";
    public const string Expired = "Billing.CheckoutExpired";
    public const string AlreadyAccepted = "Billing.CheckoutAlreadyAccepted";
    public const string NotFound = "Billing.CheckoutNotFound";

    public static bool IsAction(string action) => action is Create or Read or Accept or Recover or Candidates or Shops;
    public static string Path(Guid account) => $"{MerchantBillingDelegationV1.AccountsPath}/{account:D}/orders";
    public static string DetailPath(Guid account, Guid order) => $"{Path(account)}/{order:D}";
    public static string AcceptPath(Guid account, Guid order) => $"{DetailPath(account, order)}/accept";
    public static string OperationPath(Guid account, string kind, Guid key) => $"{Path(account)}/operations/{kind}/{key:D}";
    public static string CandidatesPath(Guid account, Guid? after) => PagePath($"{Path(account)}/eligible-shops", after);
    public static string ShopsPath(Guid account, Guid order, Guid? after) => PagePath($"{DetailPath(account, order)}/shops", after);

    private static string PagePath(string path, Guid? after) => after is { } cursor ? $"{path}/after/{cursor:D}" : path;

    // Canonical records are ordered JSON arrays. UUIDs use lower-case D and instants use UTC microseconds.
    // Operation keys select the record; actor and account authority are checked independently of the digest.
    public static string CreateHash(CreateCheckoutQuoteV1 value) => Digest(Policy, "create", value.PlanVersionId,
        value.Period, value.TermId, value.ExtraShops, value.ExpectedRevision, value.ShopIds!.Order().ToArray());

    public static string AcceptHash(Guid order, AcceptCheckoutQuoteV1 value) => Digest(Policy, "accept", order,
        value.ExpectedRevision, value.SnapshotHash, value.CustomerTermsVersion, value.RefundPolicyVersion, value.Confirmed);

    public static string ShopHash(IEnumerable<Guid> ids) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(string.Join(',', ids.Order()))));

    public static string SnapshotHash(CheckoutQuoteV1 q)
    {
        var snapshot = BaseSnapshotHash(q);
        if (q.Increase is { } change)
            return Digest(Policy, "capacity-increase", snapshot, change.QuoteHash, change.PinHash, change.RoutingRevision, change.SourceRevision);
        if (q.Reduction is { } reduction)
            return Digest(Policy, "capacity-reduction", snapshot, reduction.ScheduleId, reduction.ScheduleHash, reduction.SourceShopCount, reduction.SourceShopHash);
        if (q.Replacement is { } replacement)
            return Digest(Policy, "coverage-replacement", snapshot, replacement.ReplacementId, replacement.OutgoingShopId,
                replacement.IncomingShopId, replacement.IncomingAffiliationId, replacement.ConsentEvent);
        return q.FreshStart is { } fresh
            ? Digest(Policy, "fresh-start", snapshot, fresh.Policy, fresh.TrialShopCount, fresh.TrialSourceHash) : snapshot;
    }

    private static string BaseSnapshotHash(CheckoutQuoteV1 q) => Digest(Policy, "snapshot", q.AccountId, q.OrderId,
        q.Revision, q.PlanId, q.PlanVersionId, q.PlanVersion, q.PlanName, q.Period, q.TermId, q.Capacity,
        q.MembersPerShop, q.ShopCount, q.ShopHash, q.SourceHash, q.AmountPaise, q.Currency, q.PriceBasis,
        q.CustomerTermsVersion, q.RefundPolicyVersion, q.BillingTimeZone, q.CalendarVersion, q.RenewalPolicyVersion,
        q.StartRule, Instant(q.PreviewStartsAt), Instant(q.PreviewEndsAt), q.GraceDaysIncluded, q.AnchorLocal,
        q.Ordinal, Instant(q.CreatedAt), Instant(q.ExpiresAt), q.DevelopmentFixture);

    public static string Digest(params object?[] facts) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(facts)));

    public static string Instant(DateTimeOffset instant) =>
        instant.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);

    public static bool IsHash(string? value) => value is { Length: 64 } &&
        value.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateCheckoutQuoteV1([property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("planVersionId")] Guid PlanVersionId,
    [property: JsonPropertyName("period")] string Period,
    [property: JsonPropertyName("shopIds")] Guid[]? ShopIds,
    [property: JsonPropertyName("extraShops")] int ExtraShops,
    [property: JsonPropertyName("expectedRevision")] int ExpectedRevision,
    [property: JsonPropertyName("termId")] Guid? TermId = null)
{
    public bool IsValid() => OperationKey != Guid.Empty && PlanVersionId != Guid.Empty && Period is "Month" or "Year" &&
        ExpectedRevision > 0 && ExtraShops is >= 0 and <= CheckoutOrdersV1.MaxShops &&
        ShopIds is { Length: <= CheckoutOrdersV1.MaxShops } shops && !shops.Contains(Guid.Empty) &&
        shops.Distinct().Count() == shops.Length &&
        (TermId is null ? shops.Length > 0 : TermId != Guid.Empty && shops.Length == 0);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AcceptCheckoutQuoteV1([property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("expectedRevision")] int ExpectedRevision,
    [property: JsonPropertyName("snapshotHash")] string SnapshotHash,
    [property: JsonPropertyName("customerTermsVersion")] string CustomerTermsVersion,
    [property: JsonPropertyName("refundPolicyVersion")] string RefundPolicyVersion,
    [property: JsonPropertyName("confirmed")] bool Confirmed)
{
    public bool IsValid() => OperationKey != Guid.Empty && ExpectedRevision > 0 && Confirmed &&
        CheckoutOrdersV1.IsHash(SnapshotHash) && CustomerTermsVersion is { Length: > 0 and <= 80 } &&
        RefundPolicyVersion is { Length: > 0 and <= 80 };
}

public sealed record CheckoutQuoteV1(Guid AccountId, Guid OrderId, int Revision, Guid PlanId, Guid PlanVersionId,
    int PlanVersion, string PlanName, string Period, Guid? TermId, int Capacity, int MembersPerShop, int ShopCount,
    string ShopHash, string SourceHash, long AmountPaise, string Currency, string PriceBasis, string CustomerTermsVersion,
    string RefundPolicyVersion, string BillingTimeZone, string CalendarVersion, string RenewalPolicyVersion, string StartRule,
    DateTimeOffset PreviewStartsAt, DateTimeOffset PreviewEndsAt, int GraceDaysIncluded, string AnchorLocal, int Ordinal,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, [property: JsonRequired] bool DevelopmentFixture, string SnapshotHash,
    CheckoutAcceptanceV1? Acceptance = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CheckoutFreshStartV1? FreshStart = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CapacityIncreaseOrderV1? Increase = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CapacityReductionOrderV1? Reduction = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CoverageReplacementOrderV1? Replacement = null)
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = 1;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CheckoutFreshStartV1(string Policy, int TrialShopCount, string TrialSourceHash);

public sealed record CheckoutAcceptanceV1(Guid OperationKey, int QuoteRevision, string SnapshotHash, DateTimeOffset AcceptedAt);
public sealed record CheckoutShopV1(Guid ShopId, string Name, Guid? TermId);
public sealed record CheckoutShopPageV1(Guid AccountId, Guid? OrderId, CheckoutShopV1[] Items, Guid? NextCursor);

public sealed record CapacityIncreaseOrderV1(string QuoteHash, string PinHash, long RoutingRevision, long SourceRevision)
{
    public const string Rule = "CapacityIncrease";
}

public sealed record CapacityReductionOrderV1(Guid ScheduleId, string ScheduleHash, int SourceShopCount, string SourceShopHash);
