using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ErrorOr;
using PermitErrors = Intelibill.Application.Common.Errors.Errors.OfflinePermit;

namespace Intelibill.Application.Common.Licensing.Offline;

/// <summary>
/// RS256 compact form of the offline evidence: <c>header.payload.signature</c>, each base64url. The
/// protected header names one purpose (<c>typ</c>) and key (<c>kid</c>), so a grant is never accepted
/// as a permit. Verification needs only archived public material and has no replay window: it proves
/// what was signed, and the caller judges the signed interval at its own instant.
/// </summary>
public static class OfflinePermitSignatures
{
    public const int MinimumKeyBits = 2048;

    public static string SigningInput(string type, string keyId, ReadOnlySpan<byte> payload) =>
        Base64Url.EncodeToString(Header(type, keyId)) + "." + Base64Url.EncodeToString(payload);

    public static string Attach(string signingInput, ReadOnlySpan<byte> signature) =>
        signingInput + "." + Base64Url.EncodeToString(signature);

    /// <summary>The key identifier a token claims, before anything about it is trusted.</summary>
    public static string? ReadKeyId(string? token, string type) => Parse(token, type)?.KeyId;

    /// <summary>The signed payload bytes when <paramref name="publicKey"/> (SPKI) signed this token for this purpose.</summary>
    public static ErrorOr<byte[]> Verify(string? token, string type, ReadOnlySpan<byte> publicKey)
    {
        if (Parse(token, type) is not { } parts)
            return PermitErrors.Malformed;

        using var rsa = RSA.Create();
        try
        {
            rsa.ImportSubjectPublicKeyInfo(publicKey, out var read);
            if (read != publicKey.Length || rsa.KeySize < MinimumKeyBits)
                return PermitErrors.InvalidSignature;
        }
        catch (CryptographicException)
        {
            return PermitErrors.InvalidSignature;
        }

        return rsa.VerifyData(Encoding.ASCII.GetBytes(parts.SigningInput), parts.Signature, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1)
            ? parts.Payload
            : PermitErrors.InvalidSignature;
    }

    /// <summary>
    /// Historical proof of a permit: the retained key for its identifier signed exactly these canonical
    /// bytes, they name the expected shop, user, session, device and lease, and <paramref name="at"/> lies
    /// inside the signed window. Nothing current is consulted.
    /// </summary>
    public static ErrorOr<OfflineLicensePermitV1> VerifyPermit(string? token, OfflinePermitScope expected, DateTimeOffset at,
        Func<string, byte[]?> retainedKey)
    {
        if (ReadKeyId(token, OfflinePermitContractV1.PermitType) is not { } keyId)
            return PermitErrors.Malformed;
        if (retainedKey(keyId) is not { } publicKey)
            return PermitErrors.UnknownKey;

        var payload = Verify(token, OfflinePermitContractV1.PermitType, publicKey);
        if (payload.IsError)
            return payload.Errors;
        if (OfflinePermitContractV1.DecodePermit(payload.Value) is not { } permit)
            return PermitErrors.Malformed;
        if (permit.ShopId != expected.ShopId || permit.UserId != expected.UserId || permit.SessionId != expected.SessionId ||
            !string.Equals(permit.DeviceId, expected.DeviceId, StringComparison.Ordinal) || permit.LeaseId != expected.LeaseId)
            return PermitErrors.ScopeMismatch;

        return permit.IssuedAt == permit.NotBefore && OfflineLicencePermitPolicy.Covers(permit.NotBefore, permit.ValidUntil, at)
            ? permit
            : PermitErrors.OutsideValidity;
    }

    public static string Fingerprint(ReadOnlySpan<byte> publicKey) => Convert.ToHexStringLower(SHA256.HashData(publicKey));

    private static byte[] Header(string type, string keyId) =>
        Encoding.UTF8.GetBytes($$"""{"alg":"RS256","typ":"{{type}}","kid":"{{keyId}}"}""");

    private static Parts? Parse(string? token, string type)
    {
        if (token is not { Length: > 0 and <= OfflinePermitContractV1.MaxTokenLength } || token.Count(c => c == '.') != 2)
            return null;

        var segments = token.Split('.');
        if (Decode(segments[0]) is not { } header || Decode(segments[1]) is not { } payload ||
            Decode(segments[2]) is not { Length: > 0 } signature || ReadKid(header) is not { } keyId ||
            !header.AsSpan().SequenceEqual(Header(type, keyId)))
            return null;

        return new Parts(keyId, payload, signature, segments[0] + "." + segments[1]);
    }

    private static string? ReadKid(byte[] header)
    {
        try
        {
            using var json = JsonDocument.Parse(header);
            return json.RootElement.ValueKind == JsonValueKind.Object &&
                json.RootElement.TryGetProperty("kid", out var kid) && kid.ValueKind == JsonValueKind.String &&
                kid.GetString() is { Length: > 0 and <= 128 } value &&
                value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
                ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Only the canonical unpadded alphabet round-trips; anything else is a different token.
    private static byte[]? Decode(string segment)
    {
        if (segment.Length == 0 || !Base64Url.IsValid(segment))
            return null;
        var bytes = Base64Url.DecodeFromChars(segment);
        return Base64Url.EncodeToString(bytes) == segment ? bytes : null;
    }

    private sealed record Parts(string KeyId, byte[] Payload, byte[] Signature, string SigningInput);
}
