using System.Buffers.Text;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Shared.Security;

public sealed record ServiceAssertionPurpose(string Type, string Issuer, string Audience);
public sealed record VerifiedServiceAssertion(Guid ReplayId, string KeyId, string PublicKey, DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt, string Digest, string Assertion);

public static class SignedServiceAssertion
{
    public static ServiceAssertionPurpose LicensingDelivery { get; } =
        new("intelibill-entitlement-snapshot+jwt", "intelibill-licensing-entitlements", "intelibill-merchant-entitlements");
    public static ServiceAssertionPurpose MerchantEvents { get; } =
        new("intelibill-entitlement-merchant-event+jwt", "intelibill-merchant-entitlements", "intelibill-licensing-entitlements");

    private const int LifetimeSeconds = 60;
    public const int TokenLimit = 4096;

    public static async Task<string> SignAsync(ServiceAssertionPurpose purpose, string keyId, string path, byte[] body,
        TimeProvider clock, Func<byte[], CancellationToken, ValueTask<byte[]>> sign, CancellationToken ct)
    {
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var header = new Header("RS256", purpose.Type, keyId);
        var claims = new Claims(1, purpose.Issuer, purpose.Audience, Guid.NewGuid(), now, now,
            now + LifetimeSeconds, "POST", path, Digest(body));
        var input = Encode(JsonSerializer.SerializeToUtf8Bytes(header, EntitlementSnapshotsV1.Json)) + "." +
            Encode(JsonSerializer.SerializeToUtf8Bytes(claims, EntitlementSnapshotsV1.Json));
        return input + "." + Encode(await sign(Encoding.ASCII.GetBytes(input), ct));
    }

    public static VerifiedServiceAssertion? Verify(string? token, ServiceAssertionPurpose purpose, string path,
        byte[] body, TimeProvider clock, Func<string, string?> resolve)
    {
        if (token is null || token.Length > TokenLimit || token.Split('.') is not [var head, var data, var signature])
            return null;
        try
        {
            var header = JsonSerializer.Deserialize<Header>(Decode(head), EntitlementSnapshotsV1.Json);
            var claims = JsonSerializer.Deserialize<Claims>(Decode(data), EntitlementSnapshotsV1.Json);
            if (header is not { Alg: "RS256" } || header.Typ != purpose.Type ||
                !MerchantBillingDelegationV1.IsKeyId(header.Kid) || claims is null ||
                !Matches(claims, purpose, path, Digest(body), clock.GetUtcNow().ToUnixTimeSeconds()))
                return null;

            var pem = resolve(header.Kid);
            if (pem is null) return null;
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            if (rsa.KeySize is < 2048 or > 4096 || !rsa.VerifyData(Encoding.ASCII.GetBytes(head + "." + data),
                Decode(signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                return null;
            return new(claims.Jti, header.Kid, rsa.ExportSubjectPublicKeyInfoPem(),
                DateTimeOffset.FromUnixTimeSeconds(claims.Iat), DateTimeOffset.FromUnixTimeSeconds(claims.Exp),
                claims.Digest, token);
        }
        catch (Exception error) when (error is FormatException or CryptographicException or ArgumentException or JsonException)
        {
            return null;
        }
    }

    private static bool Matches(Claims claims, ServiceAssertionPurpose purpose, string path, string digest, long now) =>
        claims.V == 1 && claims.Iss == purpose.Issuer && claims.Aud == purpose.Audience && claims.Jti != Guid.Empty &&
        claims.Method == "POST" && claims.Path == path && claims.Digest == digest &&
        claims.Iat >= now - LifetimeSeconds && claims.Iat <= now && claims.Nbf == claims.Iat &&
        claims.Exp > now && claims.Exp <= claims.Iat + LifetimeSeconds;

    public static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static byte[] Decode(string text)
    {
        if (text.Length == 0 || text.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new FormatException();
        var bytes = Base64Url.DecodeFromChars(text);
        if (Encode(bytes) != text) throw new FormatException();
        return bytes;
    }

    private static string Encode(byte[] bytes) => Base64Url.EncodeToString(bytes);

    private sealed record Header(string Alg, string Typ, string Kid);
    private sealed record Claims(int V, string Iss, string Aud, Guid Jti, long Iat, long Nbf, long Exp,
        string Method, string Path, [property: JsonPropertyName("body_sha256")] string Digest);
}
