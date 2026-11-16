using System.Text.Json.Serialization;

namespace Intelibill.Shared.Contracts.V1;

public static class ShopTrialsV1
{
    public const int DurationDays = 14;
    public const int MaximumBodyBytes = 2048;
    public const string TimeZone = "Asia/Kolkata";
    public const string PreviewAction = "licensing.shop-trial.preview";
    public const string DeliveryPath = "/internal/licensing/v1/customer/shop-trial-consents";
    public const string DeliverySection = "ShopTrialDelivery";
    public const string DeliveryType = "intelibill-shop-trial-consent+jwt";
    public const string DeliveryIssuer = "InteliBill.Merchant.ShopTrial";
    public const string DeliveryAudience = "InteliBill.Licensing.ShopTrial";

    public static string PreviewPath(Guid shop) => $"{MerchantBillingDelegationV1.InternalPrefix}/shops/{shop:D}/trial";

    public static bool Valid(ShopTrialAcceptanceV1? request) => request is
        { TermsAccepted: true, PolicyRevision: > 0, TermsVersion.Length: > 0 and <= 80 }
        && request.OperationKey != Guid.Empty && request.PolicyId != Guid.Empty
        && !string.IsNullOrWhiteSpace(request.TermsVersion) && !request.TermsVersion.Any(char.IsControl);
}

public sealed record ShopTrialAcceptanceV1(
    [property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("policyId")] Guid PolicyId,
    [property: JsonPropertyName("policyRevision")] int PolicyRevision,
    [property: JsonPropertyName("termsVersion")] string TermsVersion,
    [property: JsonPropertyName("termsAccepted")] bool TermsAccepted);

public sealed record ShopTrialPolicyV1(Guid PolicyId, int PolicyRevision, string TermsVersion, int MemberCap,
    int AccountTrialCap, bool DevelopmentFixture);

public sealed record ShopTrialPreviewV1(Guid ShopId, ShopTrialPolicyV1? Policy, bool Eligible, string? Reason,
    DateTimeOffset EvaluatedAt, DateTimeOffset ProspectiveEnd, int? ActiveMembers = null);

public sealed record ShopTrialConsentV1(
    [property: JsonPropertyName("eventId")] Guid EventId,
    [property: JsonPropertyName("shopId")] Guid ShopId,
    [property: JsonPropertyName("actorId")] Guid ActorId,
    [property: JsonPropertyName("membershipId")] Guid MembershipId,
    [property: JsonPropertyName("authorityVersion")] long AuthorityVersion,
    [property: JsonPropertyName("activeMembers")] int ActiveMembers,
    [property: JsonPropertyName("acceptedAt")] DateTimeOffset AcceptedAt,
    [property: JsonPropertyName("acceptance")] ShopTrialAcceptanceV1 Acceptance);

public sealed record ShopTrialDeliveryResultV1(Guid EventId, string Status, string? Reason);

public sealed record ShopTrialOperationV1(Guid OperationKey, string Status, string? Reason,
    DateTimeOffset AcceptedAt, DateTimeOffset EndsAt);
