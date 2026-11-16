using System.Security.Cryptography;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Shared.Security;

public sealed class EntitlementTransportOptions
{
    public bool Enabled { get; init; }
    public string? PeerOrigin { get; init; }
    public string? LocalKeyId { get; init; }
    public string? LocalPrivateKeyPem { get; init; }
    public string? KeyVaultKeyId { get; init; }
    public string? ManagedIdentityClientId { get; init; }
    public Dictionary<string, string> PublicKeys { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> RetiredPublicKeys { get; init; } = new(StringComparer.Ordinal);

    public Uri? Origin(bool local) =>
        Enabled && Uri.TryCreate(PeerOrigin, UriKind.Absolute, out var uri) && uri.IsWellFormedOriginalString() &&
        uri.AbsolutePath == "/" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
        (uri.Scheme == Uri.UriSchemeHttps || local && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback) ? uri : null;

    public string? VerificationKey(string key, IEnumerable<string?> otherPurposeKeys) =>
        Enabled && !RetiredPublicKeys.ContainsKey(key) && PublicKeys.TryGetValue(key, out var pem) &&
        IsDistinct(pem, otherPurposeKeys.Append(LocalPrivateKeyPem)) ? pem : null;

    public async Task<string?> TrySignLocalAsync(ServiceAssertionPurpose purpose, string path, byte[] body,
        TimeProvider clock, bool local, IEnumerable<string?> otherPurposeKeys, CancellationToken ct)
    {
        if (!local || !Enabled || KeyVaultKeyId is not null || LocalPrivateKeyPem is not { } pem ||
            !MerchantBillingDelegationV1.IsKeyId(LocalKeyId) ||
            !IsDistinct(pem, otherPurposeKeys.Concat(PublicKeys.Values).Concat(RetiredPublicKeys.Values)))
            return null;
        using var rsa = ReadRsa(pem);
        return rsa is null ? null : await SignedServiceAssertion.SignAsync(purpose, LocalKeyId!, path, body, clock,
            (input, _) => ValueTask.FromResult(rsa.SignData(input, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)), ct);
    }

    public static bool IsDistinct(string pem, IEnumerable<string?> otherKeys)
    {
        using var candidate = ReadRsa(pem);
        if (candidate is null)
            return false;
        var material = candidate.ExportSubjectPublicKeyInfo();
        foreach (var other in otherKeys)
        {
            using var rsa = other is null ? null : ReadRsa(other);
            if (rsa is not null && material.AsSpan().SequenceEqual(rsa.ExportSubjectPublicKeyInfo()))
                return false;
        }
        return true;
    }

    private static RSA? ReadRsa(string pem)
    {
        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
            if (rsa.KeySize is >= 2048 and <= 4096) return rsa;
        }
        catch (Exception error) when (error is CryptographicException or ArgumentException)
        {
            // A malformed configured key disables this trust path.
        }
        rsa.Dispose();
        return null;
    }
}
