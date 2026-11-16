using System.Text.Json.Serialization;

namespace Intelibill.Shared.Contracts.V1;

public static class ShopAffiliationsV1
{
    public const string Read = "billing.affiliation.list";
    public const string Request = "billing.affiliation.request";
    public const string OwnerRead = "billing.affiliation.owner-read";
    public const string DeliveryPath = MerchantBillingDelegationV1.InternalPrefix + "/affiliation-decisions";
    public const int PageSize = 100;
    public const int ResponseLimit = 512 * 1024;

    public static string AccountPath(Guid account) => $"{MerchantBillingDelegationV1.AccountsPath}/{account:D}/affiliations";
    public static string OwnerPath(Guid account, Guid shop, Guid request) => $"{AccountPath(account)}/{shop:D}/{request:D}/owner";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequestShopAffiliationV1(
    [property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("shopId")] Guid ShopId,
    [property: JsonPropertyName("termId")] Guid? TermId,
    [property: JsonPropertyName("withoutCoverage")] bool WithoutCoverage);

public sealed record SubscriptionCoverageV1(Guid TermId, string Mode, int Capacity, DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);

public sealed record ShopAffiliationRequestV1(Guid RequestId, Guid AccountId, string AccountName, Guid ShopId,
    SubscriptionCoverageV1? Term, DateTimeOffset ExpiresAt, string Status, string? ShopName,
    string? OwnerName, DateTimeOffset? AppliedAt);

public sealed record AccountAffiliationsV1(Guid AccountId, string AccountName,
    ShopAffiliationRequestV1[] Requests, SubscriptionCoverageV1[] Terms);

public sealed record ShopAffiliationConsentV1(ShopAffiliationRequestV1 Request, string ShopName, string OwnerName,
    string Status);

public sealed record ShopAffiliationDecisionV1(
    [property: JsonPropertyName("eventId")] Guid EventId,
    [property: JsonPropertyName("requestId")] Guid RequestId,
    [property: JsonPropertyName("accountId")] Guid AccountId,
    [property: JsonPropertyName("shopId")] Guid ShopId,
    [property: JsonPropertyName("termId")] Guid? TermId,
    [property: JsonPropertyName("actorId")] Guid ActorId,
    [property: JsonPropertyName("membershipId")] Guid MembershipId,
    [property: JsonPropertyName("authorityVersion")] long AuthorityVersion,
    [property: JsonPropertyName("shopName")] string ShopName,
    [property: JsonPropertyName("ownerName")] string OwnerName,
    [property: JsonPropertyName("acceptedAt")] DateTimeOffset AcceptedAt);

public sealed record ShopAffiliationAcknowledgementV1(Guid EventId, string Status);

public static class ShopAffiliationDeliveryV1
{
    public const string Section = "ShopAffiliationDelivery";
    public const string Issuer = "InteliBill.Merchant.Affiliation";
    public const string Audience = "InteliBill.Licensing.Affiliation";
    public const string TokenType = "intelibill-shop-affiliation+jwt";
    public const int LifetimeSeconds = 60;
    public const int MaximumBodyBytes = 8192;
}
