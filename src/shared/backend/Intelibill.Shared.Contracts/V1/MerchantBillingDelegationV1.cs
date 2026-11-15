using System.Text.Json.Serialization;

namespace Intelibill.Shared.Contracts.V1;

public static class MerchantBillingDelegationV1
{
    public const int Version = 1;
    public const string Issuer = "InteliBill.Merchant.Billing";
    public const string Audience = "InteliBill.Licensing.Customer";
    public const string TokenType = "intelibill-merchant-billing-delegation+jwt";
    public const string ReadAccount = "billing.account.read";
    public const string RequestIdHeader = "X-Billing-Request-Id";
    public const string InternalPrefix = "/internal/licensing/v1/customer";
    public const string EmptyBodySha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    public const int LifetimeSeconds = 60;
    public const int MaximumTokenLength = 4096;

    public static string AccountAccessPath(Guid accountId) => $"{InternalPrefix}/accounts/{accountId:D}/access";

    public static bool IsKeyId(string? value) => value is { Length: > 0 and <= 128 }
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}

public sealed record MerchantBillingAssertionV1(
    [property: JsonPropertyName("sub")] Guid ActorId,
    [property: JsonPropertyName("account_id")] Guid AccountId,
    [property: JsonPropertyName("request_id")] Guid RequestId,
    [property: JsonPropertyName("jti")] Guid ReplayId,
    [property: JsonPropertyName("iat")] long IssuedAt,
    [property: JsonPropertyName("exp")] long ExpiresAt)
{
    [JsonPropertyName("v")]
    public int Version { get; } = MerchantBillingDelegationV1.Version;

    [JsonPropertyName("iss")]
    public string Issuer { get; } = MerchantBillingDelegationV1.Issuer;

    [JsonPropertyName("aud")]
    public string Audience { get; } = MerchantBillingDelegationV1.Audience;

    [JsonPropertyName("verified_identity")]
    public bool VerifiedIdentity { get; } = true;

    [JsonPropertyName("action")]
    public string Action { get; } = MerchantBillingDelegationV1.ReadAccount;

    [JsonPropertyName("nbf")]
    public long NotBefore => IssuedAt;

    [JsonPropertyName("method")]
    public string Method { get; } = "GET";

    [JsonPropertyName("path")]
    public string Path => MerchantBillingDelegationV1.AccountAccessPath(AccountId);

    [JsonPropertyName("body_sha256")]
    public string BodySha256 { get; } = MerchantBillingDelegationV1.EmptyBodySha256;
}

public sealed record BillingAccountAccessV1(Guid AccountId, string[] Actions);
