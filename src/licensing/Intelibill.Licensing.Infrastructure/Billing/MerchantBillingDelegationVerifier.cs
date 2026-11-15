using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Intelibill.Licensing.Infrastructure.Billing;

public interface IMerchantBillingDelegationVerifier
{
    VerifiedBillingDelegation? Verify(string token);
}

internal sealed class MerchantBillingDelegationVerifier(
    IOptionsMonitor<MerchantBillingVerificationOptions> options, TimeProvider clock) : IMerchantBillingDelegationVerifier
{
    private static readonly string[] HeaderNames = ["alg", "typ", "kid"];
    private static readonly string[] PayloadNames =
    [
        "v", "iss", "aud", "sub", "verified_identity", "account_id", "action", "request_id",
        "jti", "iat", "nbf", "exp", "method", "path", "body_sha256",
    ];
    // Actor-scoped actions (account creation and listing) carry the same fields without an account.
    private static readonly string[] ActorPayloadNames = [.. PayloadNames.Where(name => name != "account_id")];

    public VerifiedBillingDelegation? Verify(string token)
    {
        if (token.Length > MerchantBillingDelegationV1.MaximumTokenLength ||
            token.Split('.') is not [var headerPart, var payloadPart, var signaturePart] ||
            !IsBase64Url(headerPart) || !IsBase64Url(payloadPart) || !IsBase64Url(signaturePart))
            return null;

        try
        {
            using var header = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(headerPart));
            var keyId = ReadKeyId(header.RootElement);
            if (keyId is null || !options.CurrentValue.PublicKeys.TryGetValue(keyId, out var pem) ||
                !VerifySignature(pem, headerPart + "." + payloadPart, Base64UrlEncoder.DecodeBytes(signaturePart)))
                return null;

            using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(payloadPart));
            return ReadPayload(payload.RootElement, keyId, clock.GetUtcNow().ToUnixTimeSeconds());
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException or CryptographicException)
        {
            return null;
        }
    }

    private static string? ReadKeyId(JsonElement header) =>
        HasExactFields(header, HeaderNames) &&
        ReadString(header, "alg") == "RS256" &&
        ReadString(header, "typ") == MerchantBillingDelegationV1.TokenType &&
        ReadString(header, "kid") is { } kid && MerchantBillingDelegationV1.IsKeyId(kid) ? kid : null;

    private static bool VerifySignature(string pem, string input, byte[] signature)
    {
        if (!PemEncoding.TryFind(pem, out var fields) ||
            !pem.AsSpan()[fields.Label].SequenceEqual("PUBLIC KEY"))
            return false;

        var der = Convert.FromBase64String(pem[fields.Base64Data]);
        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(der, out var bytesRead);
        return bytesRead == der.Length && rsa.KeySize is >= 2048 and <= 4096 &&
            rsa.VerifyData(Encoding.ASCII.GetBytes(input), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    private static VerifiedBillingDelegation? ReadPayload(JsonElement data, string keyId, long now)
    {
        if (data.ValueKind != JsonValueKind.Object)
            return null;

        var accountScoped = data.TryGetProperty("account_id", out _);
        Guid account = Guid.Empty;
        if (!HasExactFields(data, accountScoped ? PayloadNames : ActorPayloadNames) ||
            ReadString(data, "iss") != MerchantBillingDelegationV1.Issuer ||
            ReadString(data, "aud") != MerchantBillingDelegationV1.Audience ||
            !ReadInteger(data, "v", out var version) || version != MerchantBillingDelegationV1.Version ||
            data.GetProperty("verified_identity").ValueKind != JsonValueKind.True ||
            !ReadGuid(data, "sub", out var actor) || (accountScoped && !ReadGuid(data, "account_id", out account)) ||
            !ReadGuid(data, "request_id", out var request) || !ReadGuid(data, "jti", out var replay) || request == replay ||
            !ReadInteger(data, "iat", out var issued) || !ReadInteger(data, "nbf", out var start) ||
            !ReadInteger(data, "exp", out var expiry) || issued < 0 || start != issued ||
            issued > now || expiry <= now || expiry - issued > MerchantBillingDelegationV1.LifetimeSeconds)
            return null;

        var action = ReadString(data, "action");
        var method = ReadString(data, "method");
        var path = ReadString(data, "path");
        var digest = ReadString(data, "body_sha256");
        // The scope must match the action: an account read names its account, creation and listing never do.
        if (action is null || method is null || path is null || digest is null ||
            accountScoped == MerchantBillingDelegationV1.IsActorScoped(action))
            return null;

        return new VerifiedBillingDelegation(actor, accountScoped ? account : null, action, request, replay, keyId,
            DateTimeOffset.FromUnixTimeSeconds(issued), DateTimeOffset.FromUnixTimeSeconds(expiry), method, path, digest);
    }

    private static bool HasExactFields(JsonElement element, string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        var remaining = new HashSet<string>(names, StringComparer.Ordinal);
        return element.EnumerateObject().All(property => remaining.Remove(property.Name)) && remaining.Count == 0;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.GetProperty(name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static bool ReadInteger(JsonElement element, string name, out long value)
    {
        value = 0;
        return element.GetProperty(name) is { ValueKind: JsonValueKind.Number } number && number.TryGetInt64(out value);
    }

    private static bool ReadGuid(JsonElement element, string name, out Guid value) =>
        Guid.TryParseExact(ReadString(element, name), "D", out value) && value != Guid.Empty;

    private static bool IsBase64Url(string value) =>
        value.Length > 0 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
