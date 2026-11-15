using System.Text.Json.Serialization;

namespace Intelibill.Shared.Contracts.V1;

public static class MerchantBillingDelegationV1
{
    public const int Version = 1;
    public const string Issuer = "InteliBill.Merchant.Billing";
    public const string Audience = "InteliBill.Licensing.Customer";
    public const string TokenType = "intelibill-merchant-billing-delegation+jwt";
    public const string ReadAccount = "billing.account.read";
    public const string CreateAccount = "billing.account.create";
    public const string ListAccounts = "billing.account.list";
    public const string RequestIdHeader = "X-Billing-Request-Id";
    public const string InternalPrefix = "/internal/licensing/v1/customer";
    public const string EmptyBodySha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    public const int LifetimeSeconds = 60;
    public const int MaximumTokenLength = 4096;

    public const string AccountsPath = InternalPrefix + "/accounts";
    public const int MaximumAccountNameLength = 120;
    public const int MaximumCreateBodyBytes = 1024;
    public const int MaximumListedAccounts = 100;
    public const string OwnerRole = "Owner";
    public const string ManagerRole = "Manager";

    public static string AccountAccessPath(Guid accountId) => $"{AccountsPath}/{accountId:D}/access";

    /// <summary>Actions that act for the verified actor before any account exists, so they carry no account.</summary>
    public static bool IsActorScoped(string? action) => action is CreateAccount or ListAccounts;

    /// <summary>Trimmed, 1 to 120 characters, NFC, no control characters; otherwise null. Both services apply it.</summary>
    public static string? NormalizeAccountName(string? name)
    {
        var trimmed = name?.Trim();
        return trimmed is { Length: > 0 and <= MaximumAccountNameLength }
            && !trimmed.Any(char.IsControl) && trimmed.IsNormalized(System.Text.NormalizationForm.FormC)
            ? trimmed : null;
    }

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

/// <summary>
/// Actor-scoped assertion for <see cref="MerchantBillingDelegationV1.CreateAccount"/> and
/// <see cref="MerchantBillingDelegationV1.ListAccounts"/>. Same fields as the account read without <c>account_id</c>.
/// </summary>
public sealed record MerchantBillingActorAssertionV1(
    [property: JsonPropertyName("sub")] Guid ActorId,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("body_sha256")] string BodySha256,
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

    [JsonPropertyName("nbf")]
    public long NotBefore => IssuedAt;
}

public sealed record BillingAccountAccessV1(Guid AccountId, string[] Actions);

public sealed record CreateBillingAccountRequestV1(
    [property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("name")] string Name);

public sealed record BillingAccountV1(
    [property: JsonPropertyName("accountId")] Guid AccountId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);

public sealed record BillingAccountListV1(
    [property: JsonPropertyName("accounts")] BillingAccountV1[] Accounts);
