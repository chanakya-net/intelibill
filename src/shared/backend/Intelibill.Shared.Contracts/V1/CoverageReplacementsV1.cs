using System.Text.Json.Serialization;

namespace Intelibill.Shared.Contracts.V1;

public static class CoverageReplacementsV1
{
    public const string Read = "billing.coverage-replacement.read";
    public const string Schedule = "billing.coverage-replacement.schedule";
    public const string Recover = "billing.coverage-replacement.recover";
    public const string OwnerRead = "billing.coverage-replacement.owner-read";
    public const string Kind = "CoverageReplacementConsent";
    public const string ReceiptPath = "/internal/licensing/v1/merchant-events/coverage-replacement-consent";
    public static string OwnerPath(Guid account, Guid shop, Guid replacement) =>
        $"{MerchantBillingDelegationV1.AccountsPath}/{account:D}/coverage-replacements/{shop:D}/{replacement:D}/owner";
    public const string AwaitingRelease = "Coverage.AwaitingOutgoingRelease";
    public const int PageSize = 50;
    public static string Path(Guid account, Guid term) =>
        $"{MerchantBillingDelegationV1.AccountsPath}/{account:D}/subscriptions/{term:D}/coverage-replacements";
    public static string ReadPath(Guid account, Guid term, int page) => page == 1 ? Path(account, term)
        : FormattableString.Invariant($"{Path(account, term)}/pages/{page}");
}

public sealed record ReplacementCoveredShopV1(Guid ShopId, string ShopName, DateTimeOffset? PermitDeadline);
public sealed record CoverageReplacementPreviewV1(Guid TermId, int Capacity, int MemberAllowance, DateTimeOffset EffectiveAt,
    bool CanSchedule, ReplacementCoveredShopV1[] CoveredShops, SlotShopV1[] EligibleShops, CoverageReplacementV1[] Replacements, bool HasMore);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScheduleCoverageReplacementV1([property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("outgoingShopId")] Guid OutgoingShopId,
    [property: JsonPropertyName("incomingShopId")] Guid IncomingShopId,
    [property: JsonPropertyName("observedCapacity")] int ObservedCapacity,
    [property: JsonPropertyName("observedMemberAllowance")] int ObservedMemberAllowance,
    [property: JsonPropertyName("observedEffectiveAt")] DateTimeOffset ObservedEffectiveAt,
    [property: JsonPropertyName("observedPermitDeadline")] DateTimeOffset? ObservedPermitDeadline,
    [property: JsonPropertyName("acknowledgedRenewal")] bool AcknowledgedRenewal,
    [property: JsonPropertyName("acknowledgedPermitWait")] bool AcknowledgedPermitWait,
    [property: JsonPropertyName("acknowledgedShopSelection")] bool AcknowledgedShopSelection);

public sealed record CoverageReplacementV1(Guid ReplacementId, Guid AccountId, Guid TermId,
    Guid OutgoingShopId, string OutgoingShopName, Guid IncomingShopId, string IncomingShopName, Guid IncomingAffiliationId,
    int Capacity, int MemberAllowance, DateTimeOffset EffectiveAt, DateTimeOffset? PermitDeadline,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, string State, string? Reason, string Activation);

public sealed record ReplacementOwnerV1(Guid ReplacementId, Guid ShopId, string ShopName,
    DateTimeOffset EffectiveAt, string State, string Status, string Activation);

public sealed record ReplacementConsentV1(int SchemaVersion, Guid EventId, Guid ReplacementId, Guid AccountId, Guid TermId,
    Guid ShopId, Guid AffiliationRequestId, Guid ActorId, Guid MembershipId, long AuthorityVersion,
    int ActiveMembers, string SeatHash, int MemberAllowance, DateTimeOffset EffectiveAt, DateTimeOffset RecordedAt, string Kind);

public sealed record CoverageReplacementOrderV1(Guid ReplacementId, Guid OutgoingShopId, Guid IncomingShopId,
    Guid IncomingAffiliationId, Guid ConsentEvent);
