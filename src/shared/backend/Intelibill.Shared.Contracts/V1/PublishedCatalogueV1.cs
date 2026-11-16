using System.Text.Json.Serialization;

namespace Intelibill.Shared.Contracts.V1;

public sealed record PublishedTrialPolicyV1(Guid VersionId, int VersionNumber, int DurationDays, int ActiveMemberCap,
    int MaxConcurrentTrialShops, string TrialTermsVersion, [property: JsonRequired] bool DevelopmentFixture);

/// <summary>Frozen customer-safe commercial terms. Custom plans are never returned in the public catalogue.</summary>
public sealed record PublishedPlanTermsV1(Guid PlanId, Guid VersionId, int VersionNumber, string Kind, string Name,
    long? MonthlyPricePaise, long? AnnualPricePaise, int IncludedShops, int MembersPerShop, [property: JsonRequired] bool ExtraShopsOffered,
    long? ExtraShopMonthlyPricePaise, long? ExtraShopAnnualPricePaise, string CustomerTermsVersion,
    string RefundPolicyVersion, PublishedTrialPolicyV1 TrialPolicy, [property: JsonRequired] bool DevelopmentFixture)
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = 1;
    [JsonRequired]
    public string Currency { get; init; } = "INR";
    [JsonRequired]
    public string PriceBasis { get; init; } = "TaxInclusiveFinal";
    [JsonRequired]
    public string TermCalendar { get; init; } = "CalendarMonthOrYear";
    [JsonRequired]
    public string BillingTimeZone { get; init; } = "Asia/Kolkata";
    [JsonRequired]
    public int EligiblePaidGraceDays { get; init; } = 7;
    [JsonRequired]
    public string[] GraceExclusions { get; init; } = ["Trial", "Complimentary", "Temporary", "Transition", "ScheduledCancellation", "Suspension"];
    [JsonRequired]
    public bool PaymentConfirmationIsTaxInvoice { get; init; }
}

public sealed record PublishedPlanVersionV1(PublishedPlanTermsV1 Terms, [property: JsonRequired] bool AvailableForNewPurchase);

public sealed record PublishedCatalogueV1(PublishedPlanTermsV1[] Items, Guid? NextCursor)
{
    public const string Read = "billing.catalogue.read";
    public const string Path = MerchantBillingDelegationV1.InternalPrefix + "/plans";
    public const int PageSize = 30;
    public const int MaxResponseBytes = PageSize * 2400 + 256;

    public const int MaxVersionResponseBytes = 2400 + 128;
    public static string VersionPath(Guid versionId) => $"{Path}/versions/{versionId:D}";

    public static string ReadPath(Guid? after) => after is { } id ? $"{Path}/after/{id:D}" : Path;
}
