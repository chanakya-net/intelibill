using System.Text.Json.Serialization;

namespace Intelibill.Shared.Contracts.V1;

/// <summary>
/// Owner-selected capacity reduction at renewal (<c>capacity-reduction/1</c>). Previews name every covered shop with
/// its live aggregate active-member count and never preselect shops. Scheduling freezes the explicit retained set
/// without altering the current paid term or roster; payment funds the renewal and application happens only at renewal.
/// </summary>
public static class CapacityReductionsV1
{
    public const string Policy = "capacity-reduction/1";
    public const string Preview = "billing.capacity-reduction.preview";
    public const string PreviewShops = "billing.capacity-reduction.preview-shops";
    public const string Schedule = "billing.capacity-reduction.schedule";
    public const string Read = "billing.capacity-reduction.read";
    public const string Recover = "billing.capacity-reduction.recover";
    public const string Shops = "billing.capacity-reduction.shops";
    public const string Resolve = "billing.capacity-reduction.resolve";

    public const int MaxPreviewCreateBytes = 1024;
    public const int MaxScheduleBytes = CheckoutOrdersV1.MaxCreateBytes;
    public const int MaxOrderBytes = 1024;
    public const int MaxResolveBytes = 1024;
    public const int MaxDetailBytes = 16384;
    public const int PageSize = CheckoutOrdersV1.PageSize;
    public const int MaxPageBytes = CheckoutOrdersV1.MaxPageBytes;
    public const int MaxInlineConflicts = 20;

    public const string Invalid = "Billing.CapacityReductionInvalid";
    public const string NotFound = "Billing.CapacityReductionNotFound";
    public const string SourceChanged = "Billing.CapacityReductionSourceChanged";
    public const string Expired = "Billing.CapacityReductionExpired";
    public const string MemberConflict = "Billing.CapacityReductionMemberConflict";
    public const string AlreadyScheduled = "Billing.CapacityReductionAlreadyScheduled";
    public const string AlreadyFunded = "Billing.CapacityReductionAlreadyFunded";
    public const string NeedsReview = "Billing.CapacityReductionNeedsReview";
    public const string Unavailable = "Billing.CapacityReductionUnavailable";

    public static readonly string[] States =
        ["Scheduled", "Funded", "Applying", "Applied", "NeedsResolution", "NeedsReview"];

    public static bool IsAction(string action) =>
        action is Preview or PreviewShops or Schedule or Read or Recover or Shops or Resolve;

    public static string BasePath(Guid account, Guid term) =>
        $"{MerchantBillingDelegationV1.AccountsPath}/{account:D}/subscriptions/{term:D}/capacity-reductions";

    public static string PreviewsPath(Guid account, Guid term) => $"{BasePath(account, term)}/previews";

    public static string PreviewShopsPath(Guid account, Guid term, Guid preview, Guid? after) => after is { } cursor
        ? $"{PreviewsPath(account, term)}/{preview:D}/shops/after/{cursor:D}"
        : $"{PreviewsPath(account, term)}/{preview:D}/shops";

    public static string RecoverPath(Guid account, Guid term, Guid key) =>
        $"{BasePath(account, term)}/operations/{key:D}";

    public static string DetailPath(Guid account, Guid term, Guid schedule) =>
        $"{BasePath(account, term)}/{schedule:D}";

    public static string ShopsPath(Guid account, Guid term, Guid schedule, Guid? after) => after is { } cursor
        ? $"{DetailPath(account, term, schedule)}/shops/after/{cursor:D}"
        : $"{DetailPath(account, term, schedule)}/shops";

    public static string OrderPath(Guid account, Guid term, Guid schedule) =>
        $"{DetailPath(account, term, schedule)}/order";

    public static string ResolvePath(Guid account, Guid term, Guid schedule) =>
        $"{DetailPath(account, term, schedule)}/resolve";

    public static string PreviewCreateHash(Guid term, CreateCapacityReductionPreviewV1 value) =>
        CheckoutOrdersV1.Digest(Policy, "preview", term, value.TargetPlanVersionId, value.TargetCapacity,
            value.ObservedCapacity, value.ObservedMemberAllowance, CheckoutOrdersV1.Instant(value.ObservedPaidThrough));

    public static string ScheduleHash(Guid term, ScheduleCapacityReductionV1 value) =>
        CheckoutOrdersV1.Digest(Policy, "schedule", term, value.PreviewId, value.PreviewSnapshotHash,
            value.RetainedShopIds.Order().ToArray(), value.CustomerTermsVersion, value.RefundPolicyVersion, value.Confirmed);

    public static string ResolveHash(Guid term, Guid schedule, ResolveCapacityReductionV1 value) =>
        CheckoutOrdersV1.Digest(Policy, "resolve", term, schedule, value.ExpectedGeneration, value.SnapshotHash);

    public static string PreviewSnapshotHash(CapacityReductionPreviewV1 p) => CheckoutOrdersV1.Digest(Policy, "preview-snapshot",
        p.AccountId, p.PreviewId, p.TermId, p.SourcePlanVersionId, p.TargetPlanId, p.TargetPlanVersionId, p.TargetPlanVersion,
        p.TargetPlanName, p.Period, p.PriorCapacity, p.TargetCapacity, p.PriorMemberAllowance, p.TargetMemberAllowance,
        p.TargetMode, p.SlotsRemoved, p.UnusedSlots, p.MinimumShopsToRemove, p.SourceShopCount, p.SourceShopHash, p.SourceHash,
        p.ObservationDigest, p.HasMemberConflicts, p.ConflictedShopCount, p.AmountPaise, p.Currency, p.PriceBasis,
        p.CustomerTermsVersion, p.RefundPolicyVersion, p.BillingTimeZone, p.CalendarVersion, p.RenewalPolicyVersion,
        p.AnchorLocal, p.Ordinal, CheckoutOrdersV1.Instant(p.CurrentStartsAt), CheckoutOrdersV1.Instant(p.CurrentPaidThrough),
        CheckoutOrdersV1.Instant(p.RenewalStartsAt), CheckoutOrdersV1.Instant(p.RenewalEndsAt), p.SuspensionDisclosed,
        CheckoutOrdersV1.Instant(p.CreatedAt), CheckoutOrdersV1.Instant(p.ExpiresAt), p.DevelopmentFixture);

    public static string ScheduleSnapshotHash(ScheduledCapacityReductionV1 s) => CheckoutOrdersV1.Digest(Policy, "schedule-snapshot",
        s.AccountId, s.ScheduleId, s.PreviewId, s.TermId, s.TargetPlanId, s.TargetPlanVersionId, s.TargetPlanVersion,
        s.TargetPlanName, s.Period, s.PriorCapacity, s.TargetCapacity, s.PriorMemberAllowance, s.TargetMemberAllowance,
        s.TargetMode, s.SourceShopCount, s.SourceShopHash, s.RetainedShopCount, s.RetainedShopHash, s.SourceHash,
        s.ObservationDigest, s.AmountPaise, s.Currency, s.PriceBasis, s.CustomerTermsVersion, s.RefundPolicyVersion,
        s.BillingTimeZone, s.CalendarVersion, s.RenewalPolicyVersion, s.AnchorLocal, s.Ordinal,
        CheckoutOrdersV1.Instant(s.CurrentStartsAt), CheckoutOrdersV1.Instant(s.CurrentPaidThrough),
        CheckoutOrdersV1.Instant(s.EffectiveAt), CheckoutOrdersV1.Instant(s.RenewalEndsAt), s.SuspensionDisclosed,
        CheckoutOrdersV1.Instant(s.CreatedAt), s.DevelopmentFixture);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateCapacityReductionPreviewV1(
    [property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("targetPlanVersionId")] Guid TargetPlanVersionId,
    [property: JsonPropertyName("targetCapacity")] int TargetCapacity,
    [property: JsonPropertyName("observedCapacity")] int ObservedCapacity,
    [property: JsonPropertyName("observedMemberAllowance")] int ObservedMemberAllowance,
    [property: JsonPropertyName("observedPaidThrough")] DateTimeOffset ObservedPaidThrough)
{
    public bool IsValid() => OperationKey != Guid.Empty && TargetPlanVersionId != Guid.Empty &&
        TargetCapacity is > 0 and <= CheckoutOrdersV1.MaxShops &&
        ObservedCapacity is > 0 and <= CheckoutOrdersV1.MaxShops &&
        ObservedMemberAllowance is > 0 and <= CheckoutOrdersV1.MaxShops &&
        TargetCapacity <= ObservedCapacity &&
        EntitlementSnapshotsV1.IsInstant(ObservedPaidThrough.ToUniversalTime());
}

public sealed record CapacityReductionPreviewV1(
    Guid AccountId, Guid PreviewId, Guid TermId, Guid? SourcePlanVersionId, Guid TargetPlanId, Guid TargetPlanVersionId,
    int TargetPlanVersion, string TargetPlanName, string Period, int PriorCapacity, int TargetCapacity,
    int PriorMemberAllowance, int TargetMemberAllowance, string TargetMode, int SlotsRemoved, int UnusedSlots,
    int MinimumShopsToRemove, int SourceShopCount, string SourceShopHash, string SourceHash, string ObservationDigest,
    bool HasMemberConflicts, int ConflictedShopCount, long AmountPaise, string Currency, string PriceBasis,
    string CustomerTermsVersion, string RefundPolicyVersion, string BillingTimeZone, string CalendarVersion,
    string RenewalPolicyVersion, string AnchorLocal, int Ordinal, DateTimeOffset CurrentStartsAt,
    DateTimeOffset CurrentPaidThrough, DateTimeOffset RenewalStartsAt, DateTimeOffset RenewalEndsAt,
    bool SuspensionDisclosed, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    [property: JsonRequired] bool DevelopmentFixture, string SnapshotHash)
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = 1;
}

public sealed record CapacityReductionShopV1(
    Guid ShopId, string Name, Guid AffiliationRequestId, int ActiveMembers, bool ExceedsAllowance, int ExcessMembers,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Retained = null);

public sealed record CapacityReductionShopPageV1(
    Guid AccountId, Guid ReferenceId, CapacityReductionShopV1[] Items, Guid? NextCursor);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScheduleCapacityReductionV1(
    [property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("previewId")] Guid PreviewId,
    [property: JsonPropertyName("previewSnapshotHash")] string PreviewSnapshotHash,
    [property: JsonPropertyName("retainedShopIds")] Guid[] RetainedShopIds,
    [property: JsonPropertyName("customerTermsVersion")] string CustomerTermsVersion,
    [property: JsonPropertyName("refundPolicyVersion")] string RefundPolicyVersion,
    [property: JsonPropertyName("confirmed")] bool Confirmed)
{
    public bool IsValid() => OperationKey != Guid.Empty && PreviewId != Guid.Empty && Confirmed &&
        CheckoutOrdersV1.IsHash(PreviewSnapshotHash) &&
        RetainedShopIds is { Length: > 0 and <= CheckoutOrdersV1.MaxShops } ids &&
        !ids.Contains(Guid.Empty) && ids.Distinct().Count() == ids.Length &&
        CustomerTermsVersion is { Length: > 0 and <= 80 } && RefundPolicyVersion is { Length: > 0 and <= 80 };
}

public sealed record ReductionConflictV1(Guid ShopId, string ShopName, string Reason, int ActiveMembers, int TargetAllowance);

public sealed record ScheduledCapacityReductionV1(
    Guid AccountId, Guid ScheduleId, Guid PreviewId, Guid TermId, string State, string? Reason,
    Guid TargetPlanId, Guid TargetPlanVersionId, int TargetPlanVersion, string TargetPlanName, string Period,
    int PriorCapacity, int TargetCapacity, int PriorMemberAllowance, int TargetMemberAllowance, string TargetMode,
    int SourceShopCount, string SourceShopHash, int RetainedShopCount, string RetainedShopHash, string SourceHash,
    string ObservationDigest, long AmountPaise, string Currency, string PriceBasis, string CustomerTermsVersion,
    string RefundPolicyVersion, string BillingTimeZone, string CalendarVersion, string RenewalPolicyVersion,
    string AnchorLocal, int Ordinal, DateTimeOffset CurrentStartsAt, DateTimeOffset CurrentPaidThrough,
    DateTimeOffset EffectiveAt, DateTimeOffset RenewalEndsAt, long ResolutionGeneration,
    Guid? OrderId, Guid? AppliedTermId, bool SuspensionDisclosed, [property: JsonRequired] bool DevelopmentFixture,
    DateTimeOffset CreatedAt, DateTimeOffset? FundedAt, DateTimeOffset? AppliedAt, string SnapshotHash,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ReductionConflictV1[]? Conflicts = null)
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = 1;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateReductionOrderV1(
    [property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("snapshotHash")] string SnapshotHash)
{
    public bool IsValid() => OperationKey != Guid.Empty && CheckoutOrdersV1.IsHash(SnapshotHash);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResolveCapacityReductionV1(
    [property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("expectedGeneration")] long ExpectedGeneration,
    [property: JsonPropertyName("snapshotHash")] string SnapshotHash)
{
    public bool IsValid() => OperationKey != Guid.Empty && ExpectedGeneration >= 0 && CheckoutOrdersV1.IsHash(SnapshotHash);
}
