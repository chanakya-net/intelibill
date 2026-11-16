using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Licensing.Application.Trials;
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
        var proof = VerifyConsent(token, ShopAffiliationDeliveryV1.Section, ShopAffiliationDeliveryV1.TokenType,
            ShopAffiliationDeliveryV1.Issuer, ShopAffiliationDeliveryV1.Audience, ShopAffiliationsV1.DeliveryPath,
            ShopTrialsV1.DeliverySection);
        return proof is null ? null : new(proof.ReplayId, proof.ExpiresAt, proof.Digest);
    }

    public TrialDeliveryProof? VerifyTrialDelivery(string token) =>
        VerifyConsent(token, ShopTrialsV1.DeliverySection, ShopTrialsV1.DeliveryType, ShopTrialsV1.DeliveryIssuer,
            ShopTrialsV1.DeliveryAudience, ShopTrialsV1.DeliveryPath, ShopAffiliationDeliveryV1.Section);

    private TrialDeliveryProof? VerifyConsent(string token, string section, string type, string issuer,
        string audience, string path, string otherDeliverySection)
    {
        if (token.Length > MerchantBillingDelegationV1.MaximumTokenLength ||
            token.Split('.') is not [var headerPart, var payloadPart, var signaturePart] ||
            !IsBase64Url(headerPart) || !IsBase64Url(payloadPart) || !IsBase64Url(signaturePart))
            return null;

        try
        {
            using var header = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(headerPart));
            var fields = header.RootElement;
            if (!HasExactFields(fields, HeaderNames) || ReadString(fields, "typ") != type ||
                ReadString(fields, "alg") != "RS256" || !MerchantBillingDelegationV1.IsKeyId(ReadString(fields, "kid")))
                return null;

            var input = headerPart + "." + payloadPart;
            var signature = Base64UrlEncoder.DecodeBytes(signaturePart);
            var settings = options.Get(section);
            if (!settings.PublicKeys.TryGetValue(ReadString(fields, "kid")!, out var pem) || !VerifySignature(pem, input, signature) ||
                options.CurrentValue.PublicKeys.Values.Concat(options.Get(otherDeliverySection).PublicKeys.Values)
                    .Any(other => VerifySignature(other, input, signature)))
                return null;

            using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(payloadPart));
            return ReadDelivery(payload.RootElement, clock.GetUtcNow().ToUnixTimeSeconds(), issuer, audience, path);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException or CryptographicException)
        {
            return null;
        }
    }

    private static TrialDeliveryProof? ReadDelivery(JsonElement fields, long now, string issuer, string audience, string path)
    {
        if (!HasExactFields(fields, DeliveryFields) || ReadString(fields, "iss") != issuer ||
            ReadString(fields, "aud") != audience ||
            !ReadInteger(fields, "v", out var version) || version != 1 ||
            !ReadGuid(fields, "jti", out var replay) || !ReadInteger(fields, "iat", out var issued) ||
            !ReadInteger(fields, "nbf", out var start) || !ReadInteger(fields, "exp", out var expiry) ||
            issued < 0 || start != issued || issued > now || expiry <= now ||
            expiry - issued > ShopAffiliationDeliveryV1.LifetimeSeconds ||
            ReadString(fields, "method") != "POST" || ReadString(fields, "path") != path ||
            ReadString(fields, "body_sha256") is not { Length: 64 } digest || !digest.All(char.IsAsciiHexDigitLower))
            return null;
        return new TrialDeliveryProof(replay, DateTimeOffset.FromUnixTimeSeconds(expiry), digest);
    }
}
