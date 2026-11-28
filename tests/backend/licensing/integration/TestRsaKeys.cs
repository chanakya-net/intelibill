using System.Security.Cryptography;

namespace Intelibill.Licensing.Integration.Tests;

internal enum TestRsaKeyPurpose
{
    BillingDelegation,
    ShopTrial,
    ShopAffiliation,
    EntitlementSource,
    EntitlementMerchant,
    General,
    Alternate,
}

/// <summary>
/// Precomputed 2048-bit RSA key pairs, one distinct key per signing purpose. Importing precomputed parameters
/// takes sub-millisecond time while preserving distinct per-purpose keys and independent disposable RSA instances.
/// </summary>
internal static class TestRsaKeys
{
    private static readonly Dictionary<TestRsaKeyPurpose, CachedKey> Keys = BuildKeys();

    internal static RSA Create(TestRsaKeyPurpose purpose) => RSA.Create(Keys[purpose].Parameters);

    internal static string PrivatePem(TestRsaKeyPurpose purpose) => Keys[purpose].PrivatePem;

    internal static string PublicPem(TestRsaKeyPurpose purpose) => Keys[purpose].PublicPem;

    private static Dictionary<TestRsaKeyPurpose, CachedKey> BuildKeys()
    {
        var result = new Dictionary<TestRsaKeyPurpose, CachedKey>();
        foreach (var purpose in Enum.GetValues<TestRsaKeyPurpose>())
        {
            using var rsa = RSA.Create(2048);
            result[purpose] = new CachedKey(
                rsa.ExportParameters(includePrivateParameters: true),
                rsa.ExportPkcs8PrivateKeyPem(),
                rsa.ExportSubjectPublicKeyInfoPem());
        }

        return result;
    }

    private sealed record CachedKey(RSAParameters Parameters, string PrivatePem, string PublicPem);
}
