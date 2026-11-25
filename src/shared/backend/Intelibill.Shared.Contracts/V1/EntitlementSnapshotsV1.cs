using System.Text.Json;
using System.Text.Json.Serialization;

namespace Intelibill.Shared.Contracts.V1;

public static class EntitlementSnapshotsV1
{
    public const int SchemaVersion = 1;
    public const string PolicyVersion = "entitlement-projection/1";
    public const string Section = "EntitlementDelivery";
    public const string SnapshotPath = "/internal/licensing/v1/entitlements";
    public const string AcknowledgementPath = "/internal/licensing/v1/merchant-events/acknowledgement";
    public const string ReconciliationPath = "/internal/licensing/v1/merchant-events/reconciliation";
    public const int BodyLimit = 8192;
    public static readonly TimeSpan Freshness = TimeSpan.FromHours(48);
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        MaxDepth = 8,
    };

    public static string? ReadRestrictionError(AccountRestrictionSnapshotV1 account) =>
        account.Revision <= 0 || !IsInstant(account.UpstreamVerifiedAt) ||
        account.Restriction is not ("Unrestricted" or "Trading" or "Security") ||
        (account.Restriction == "Unrestricted" ? account.Suspension is not null
            : account.Suspension is { } suspension && (!suspension.IsValid() || suspension.Mode != account.Restriction)) ||
        (account.ReleasedRevision is { } released && (released <= 0 || released >= account.Revision ||
            account.Restriction != "Unrestricted")) ||
        (account.PreviousReleaseRevision is { } previous && (previous <= 0 || previous >= account.Revision ||
            account.Restriction == "Unrestricted")) ? "InvalidAccountScope" : null;

    public static bool IsInstant(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero && value.Year is >= 2000 and <= 9998 && value.UtcTicks % 10 == 0;

    public static DateTimeOffset Instant(TimeProvider clock)
    {
        var ticks = clock.GetUtcNow().UtcTicks;
        return new DateTimeOffset(ticks - ticks % 10, TimeSpan.Zero);
    }

    public static bool IsDigest(string? value) =>
        value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public sealed record AccountRestrictionSnapshotV1(
    long Revision, DateTimeOffset UpstreamVerifiedAt, string Restriction, long? ReleasedRevision,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CustomerSuspensionV1? Suspension = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? PreviousReleaseRevision = null);

public sealed record CustomerSuspensionV1(string Mode, DateTimeOffset EffectiveAt, string CustomerExplanation)
{
    public bool IsValid() => Mode is "Trading" or "Security" && EntitlementSnapshotsV1.IsInstant(EffectiveAt)
        && CustomerExplanation is { Length: > 0 and <= 500 } && !string.IsNullOrWhiteSpace(CustomerExplanation)
        && !CustomerExplanation.Any(character => char.IsControl(character) && character != '\n');
}

public sealed record ShopEntitlementSnapshotV1(
    long Revision, DateTimeOffset UpstreamVerifiedAt, string State, Guid? TermId, string? Basis,
    DateTimeOffset? StartsAt, DateTimeOffset? AccessEndsAt, DateTimeOffset? NextBoundary,
    int? ShopCapacity, int? MemberAllowance,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CustomerSuspensionV1? Suspension = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ReleasedRevision = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ReopenedRevision = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ClosureRevision = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? SlotAssignmentId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ReplacementId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ReleasedReplacementId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ReplacementExceptionId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReplacementExceptionLeg = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ReplacementExceptionGeneration = null)
{
    public bool IsValid()
    {
        if (Revision <= 0 || !EntitlementSnapshotsV1.IsInstant(UpstreamVerifiedAt) ||
            (State != ShopClosuresV1.Closed && !SubscriptionStatusV1.States.Contains(State)) || Basis is not null && !SubscriptionStatusV1.Bases.Contains(Basis) ||
            StartsAt is { } start && !EntitlementSnapshotsV1.IsInstant(start) ||
            AccessEndsAt is { } end && !EntitlementSnapshotsV1.IsInstant(end) ||
            NextBoundary is { } next && (!EntitlementSnapshotsV1.IsInstant(next) || next <= UpstreamVerifiedAt) ||
            ReleasedRevision is { } release && (release <= 0 || release >= Revision) ||
            ReopenedRevision is { } reopening && (reopening <= 0 || reopening >= Revision) ||
            ClosureRevision is { } closure && (closure <= 1 || closure > Revision || closure <= ReopenedRevision) ||
            TermId == Guid.Empty || SlotAssignmentId == Guid.Empty || ReplacementId == Guid.Empty || ReleasedReplacementId == Guid.Empty ||
            ReplacementExceptionId == Guid.Empty ||
            (ReplacementExceptionId is null) != (ReplacementExceptionLeg is null) ||
            (ReplacementExceptionId is null) != (ReplacementExceptionGeneration is null) ||
            (ReplacementExceptionLeg is not null && !CoverageReplacementExceptionsV1.IsSnapshotLeg(ReplacementExceptionLeg)) ||
            ReplacementExceptionGeneration is <= 0 ||
            ShopCapacity is <= 0 || MemberAllowance is <= 0 || Suspension is { } suspension && !suspension.IsValid())
            return false;
        return State is not ("Active" or "Grace") || TermId is not null && Basis is not null &&
            StartsAt <= UpstreamVerifiedAt && AccessEndsAt > UpstreamVerifiedAt;
    }
}

public sealed record EntitlementSnapshotV1(
    int SchemaVersion, string PolicyVersion, Guid EventId, Guid AccountId, Guid ShopId, Guid AffiliationRequestId,
    AccountRestrictionSnapshotV1? Account, ShopEntitlementSnapshotV1? Shop)
{
    public bool HasValidIdentity() => SchemaVersion == EntitlementSnapshotsV1.SchemaVersion &&
        PolicyVersion == EntitlementSnapshotsV1.PolicyVersion && EventId != Guid.Empty &&
        AccountId != Guid.Empty && ShopId != Guid.Empty && AffiliationRequestId != Guid.Empty;
}

public sealed record EntitlementAcknowledgementV1(
    int SchemaVersion, Guid EventId, string Digest, Guid AccountId, Guid ShopId, Guid AffiliationRequestId,
    long? AccountRevision, long? ShopRevision, long? AppliedAccountRevision, long? AppliedShopRevision,
    string Outcome, string Disposition, DateTimeOffset AppliedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ActivatedReplacementId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ActivatedExceptionId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ActivatedExceptionLeg = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ActivatedExceptionGeneration = null);

public sealed record EntitlementReconciliationV1(
    int SchemaVersion, Guid EventId, Guid AccountId, Guid ShopId, Guid AffiliationRequestId,
    long? AccountRevision, long? ShopRevision);
