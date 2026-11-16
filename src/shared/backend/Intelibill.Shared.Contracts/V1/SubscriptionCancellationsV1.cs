using System.Text.Json.Serialization;

namespace Intelibill.Shared.Contracts.V1;

public static class SubscriptionCancellationsV1
{
    public const string Read = "billing.subscription.cancellation.read";
    public const string Schedule = "billing.subscription.cancellation.schedule";
    public const string Recover = "billing.subscription.cancellation.recover";
    public const string Policy = "term-boundary/1";
    public const int PageSize = 100;
    public const int MaximumBodyBytes = 2048;
    public const int MaximumShopNameLength = 250;
    // Each UTF-16 unit can require a six-byte JSON escape. Allow for shop IDs, structure and the envelope.
    public const int MaximumResponseBytes = PageSize * (MaximumShopNameLength * 6 + 96) + 2048;

    public static bool IsAction(string action) => action is Read or Schedule or Recover;

    public static string Path(Guid account, Guid term) =>
        $"{MerchantBillingDelegationV1.AccountsPath}/{account:D}/subscriptions/{term:D}/cancellation";

    public static string DetailPath(Guid account, Guid term, Guid? after) =>
        after is { } cursor ? $"{Path(account, term)}/after/{cursor:D}" : Path(account, term);

    public static string OperationPath(Guid account, Guid term, Guid operation) =>
        $"{Path(account, term)}/operations/{operation:D}";

    public static bool IsDigest(string? value) =>
        value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public sealed record ScheduleSubscriptionCancellationV1(
    [property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("expectedVersion")] long? ExpectedVersion,
    [property: JsonPropertyName("sourceHash")] string? SourceHash,
    [property: JsonPropertyName("acceptedEffectiveAt")] DateTimeOffset AcceptedEffectiveAt,
    [property: JsonPropertyName("affectedShopSetHash")] string? AffectedShopSetHash,
    [property: JsonPropertyName("affectedShopCount")] int AffectedShopCount,
    [property: JsonPropertyName("policyVersion")] string? PolicyVersion,
    [property: JsonPropertyName("confirmed")] bool Confirmed)
{
    public bool IsValid() => OperationKey != Guid.Empty && ExpectedVersion >= 0 && Confirmed && AffectedShopCount > 0
        && SubscriptionCancellationsV1.IsDigest(SourceHash) && SubscriptionCancellationsV1.IsDigest(AffectedShopSetHash)
        && PolicyVersion == SubscriptionCancellationsV1.Policy && EntitlementSnapshotsV1.IsInstant(AcceptedEffectiveAt);
}

public sealed record SubscriptionCancellationSummaryV1(Guid ChangeId, long Version, DateTimeOffset EffectiveAt,
    string PolicyVersion);

public sealed record CancellationShopV1(Guid ShopId, string ShopName);

public sealed record SubscriptionCancellationDetailV1(int SchemaVersion, Guid AccountId, Guid TermId, long Version,
    Guid? ChangeId, DateTimeOffset EffectiveAt, string PolicyVersion, string TimeZone, string Basis,
    SubscriptionCommercialV1? Commercial, string? SourceHash, string AffectedShopSetHash, int AffectedShopCount,
    CancellationShopV1[] AffectedShops, Guid? NextCursor, bool NoRefund, bool NoGrace);

public sealed record SubscriptionCancellationOutcomeV1(int SchemaVersion, Guid AccountId, Guid TermId, Guid OperationKey,
    SubscriptionCancellationSummaryV1 Cancellation, int AffectedShopCount);
