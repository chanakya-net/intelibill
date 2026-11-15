using System.Security.Cryptography;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Intelibill.Infrastructure.Options;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Intelibill.Infrastructure.Billing;

internal interface IMerchantBillingSigningKeys
{
    ValueTask<MerchantBillingSigningKey?> ResolveAsync(CancellationToken cancellationToken);
}

internal abstract class MerchantBillingSigningKey(string id) : IDisposable
{
    internal string Id { get; } = id;
    internal abstract ValueTask<byte[]> SignAsync(byte[] bytes, CancellationToken cancellationToken);
    public abstract void Dispose();
}

internal sealed class MerchantBillingDelegationKeys(
    IOptionsMonitor<MerchantBillingDelegationOptions> options, IOptions<JwtOptions> merchantJwt,
    IHostEnvironment environment) : IMerchantBillingSigningKeys
{
    public async ValueTask<MerchantBillingSigningKey?> ResolveAsync(CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        if (settings.LocalPrivateKeyPem is { Length: > 0 } pem)
        {
            return settings.KeyVaultKeyId is null && MerchantBillingDelegationOptions.AllowsLocalCredentials(environment)
                && MerchantBillingDelegationV1.IsKeyId(settings.LocalKeyId)
                ? ReadLocalKey(settings.LocalKeyId!, pem) : null;
        }

        if (!Uri.TryCreate(settings.KeyVaultKeyId, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            (Uri.TryCreate(merchantJwt.Value.KeyVaultKeyId, UriKind.Absolute, out var ordinaryKey)
                && string.Equals(uri.AbsoluteUri, ordinaryKey.AbsoluteUri, StringComparison.OrdinalIgnoreCase)))
            return null;

        var segments = uri.AbsolutePath.Split('/');
        if (segments.Length != 3 || segments[1] != "keys" || !MerchantBillingDelegationV1.IsKeyId(segments[2]))
            return null;

        var client = new KeyClient(new Uri(uri.GetLeftPart(UriPartial.Authority)), AzureCredentials.Create(settings.ManagedIdentityClientId));
        var key = await client.GetKeyAsync(segments[2], cancellationToken: cancellationToken);
        var version = key.Value.Properties.Version;
        return MerchantBillingDelegationV1.IsKeyId(version) && key.Value.Properties.Enabled != false
            ? new VaultSigningKey(version, client.GetCryptographyClient(segments[2], version)) : null;
    }

    private static LocalSigningKey? ReadLocalKey(string id, string pem)
    {
        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
            if (rsa.KeySize >= 2048)
                return new LocalSigningKey(id, rsa);
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            // An unusable test key leaves this boundary unavailable.
        }

        rsa.Dispose();
        return null;
    }

    private sealed class LocalSigningKey(string id, RSA rsa) : MerchantBillingSigningKey(id)
    {
        internal override ValueTask<byte[]> SignAsync(byte[] bytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }

        public override void Dispose() => rsa.Dispose();
    }

    private sealed class VaultSigningKey(string id, CryptographyClient client) : MerchantBillingSigningKey(id)
    {
        internal override async ValueTask<byte[]> SignAsync(byte[] bytes, CancellationToken cancellationToken) =>
            (await client.SignDataAsync(SignatureAlgorithm.RS256, bytes, cancellationToken)).Signature;

        public override void Dispose()
        {
        }
    }
}
