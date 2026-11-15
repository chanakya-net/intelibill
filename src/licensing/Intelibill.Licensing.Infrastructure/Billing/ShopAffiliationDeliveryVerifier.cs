using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Microsoft.IdentityModel.Tokens;

namespace Intelibill.Licensing.Infrastructure.Billing;

public sealed record VerifiedAffiliationDelivery(Guid ReplayId, DateTimeOffset ExpiresAt, string Digest);

public interface IShopAffiliationDeliveryVerifier
{
    VerifiedAffiliationDelivery? VerifyDelivery(string token);
}

internal sealed partial class MerchantBillingDelegationVerifier
{
    private static readonly string[] DeliveryFields = ["v", "iss", "aud", "jti", "iat", "nbf", "exp", "method", "path", "body_sha256"];

    public VerifiedAffiliationDelivery? VerifyDelivery(string token)
    {
        if (token.Length > MerchantBillingDelegationV1.MaximumTokenLength ||
            token.Split('.') is not [var headerPart, var payloadPart, var signaturePart] ||
            !IsBase64Url(headerPart) || !IsBase64Url(payloadPart) || !IsBase64Url(signaturePart))
            return null;

        try
        {
            using var header = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(headerPart));
            var fields = header.RootElement;
            if (!HasExactFields(fields, HeaderNames) || ReadString(fields, "typ") != ShopAffiliationDeliveryV1.TokenType ||
                ReadString(fields, "alg") != "RS256" || !MerchantBillingDelegationV1.IsKeyId(ReadString(fields, "kid")))
                return null;

            var input = headerPart + "." + payloadPart;
            var signature = Base64UrlEncoder.DecodeBytes(signaturePart);
            var settings = options.Get(ShopAffiliationDeliveryV1.Section);
            if (!settings.PublicKeys.TryGetValue(ReadString(fields, "kid")!, out var pem) || !VerifySignature(pem, input, signature) ||
                options.CurrentValue.PublicKeys.Values.Any(interactive => VerifySignature(interactive, input, signature)))
                return null;

            using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(payloadPart));
            return ReadDelivery(payload.RootElement, clock.GetUtcNow().ToUnixTimeSeconds());
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException or CryptographicException)
        {
            return null;
        }
    }

    private static VerifiedAffiliationDelivery? ReadDelivery(JsonElement fields, long now)
    {
        if (!HasExactFields(fields, DeliveryFields) || ReadString(fields, "iss") != ShopAffiliationDeliveryV1.Issuer ||
            ReadString(fields, "aud") != ShopAffiliationDeliveryV1.Audience ||
            !ReadInteger(fields, "v", out var version) || version != 1 ||
            !ReadGuid(fields, "jti", out var replay) || !ReadInteger(fields, "iat", out var issued) ||
            !ReadInteger(fields, "nbf", out var start) || !ReadInteger(fields, "exp", out var expiry) ||
            issued < 0 || start != issued || issued > now || expiry <= now ||
            expiry - issued > ShopAffiliationDeliveryV1.LifetimeSeconds ||
            ReadString(fields, "method") != "POST" || ReadString(fields, "path") != ShopAffiliationsV1.DeliveryPath ||
            ReadString(fields, "body_sha256") is not { Length: 64 } digest || !digest.All(char.IsAsciiHexDigitLower))
            return null;
        return new VerifiedAffiliationDelivery(replay, DateTimeOffset.FromUnixTimeSeconds(expiry), digest);
    }
}
