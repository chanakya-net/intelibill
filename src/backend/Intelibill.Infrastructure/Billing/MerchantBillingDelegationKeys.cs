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
    IHostEnvironment environment, TimeProvider clock, Func<Uri, string?, KeyClient> createVaultClient, string? optionsName = null)
    : IMerchantBillingSigningKeys, IDisposable
{
    private static readonly TimeSpan VersionRefreshInterval = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim _vaultGate = new(1, 1);
    private VaultKeyCache? _vault;

    public MerchantBillingDelegationKeys(IOptionsMonitor<MerchantBillingDelegationOptions> options,
        IOptions<JwtOptions> merchantJwt, IHostEnvironment environment, TimeProvider clock)
        : this(options, merchantJwt, environment, clock,
            (vault, identity) => new KeyClient(vault, AzureCredentials.Create(identity)))
    {
    }

    internal static MerchantBillingDelegationKeys ForAffiliation(IOptionsMonitor<MerchantBillingDelegationOptions> options,
        IOptions<JwtOptions> merchantJwt, IHostEnvironment environment, TimeProvider clock) =>
        new(options, merchantJwt, environment, clock,
            (vault, identity) => new KeyClient(vault, AzureCredentials.Create(identity)),
            ShopAffiliationDeliveryV1.Section);

    public async ValueTask<MerchantBillingSigningKey?> ResolveAsync(CancellationToken cancellationToken)
    {
        var settings = options.Get(optionsName);
        if (settings.LocalPrivateKeyPem is { Length: > 0 } pem)
        {
            return settings.KeyVaultKeyId is null && MerchantBillingDelegationOptions.AllowsLocalCredentials(environment)
                && MerchantBillingDelegationV1.IsKeyId(settings.LocalKeyId)
                ? ReadLocalKey(settings.LocalKeyId!, pem) : null;
        }

        return await ResolveVaultAsync(cancellationToken);
    }

    private async ValueTask<MerchantBillingSigningKey?> ResolveVaultAsync(CancellationToken cancellationToken)
    {
        await _vaultGate.WaitAsync(cancellationToken);
        try
        {
            var settings = options.Get(optionsName);
            var reference = ParseVaultReference(settings, merchantJwt.Value.KeyVaultKeyId);
            if (reference is null)
                return null;

            if (_vault?.Reference != reference)
                _vault = new VaultKeyCache(reference, createVaultClient(reference.Vault, reference.ManagedIdentityClientId));
            return await ResolveVersionAsync(_vault, cancellationToken);
        }
        finally
        {
            _vaultGate.Release();
        }
    }

    private async ValueTask<VaultSigningKey?> ResolveVersionAsync(VaultKeyCache cache, CancellationToken cancellationToken)
    {
        var reference = cache.Reference;
        if (reference.Version is { } pinned)
            return cache.SigningKey ??= new VaultSigningKey(pinned, cache.Client.GetCryptographyClient(reference.Name, pinned));

        if (clock.GetUtcNow() < cache.RefreshAt)
            return cache.SigningKey;

        var key = await cache.Client.GetKeyAsync(reference.Name, cancellationToken: cancellationToken);
        var version = key.Value.Properties.Version;
        if (!MerchantBillingDelegationV1.IsKeyId(version) || key.Value.Properties.Enabled == false)
            cache.SigningKey = null;
        else if (cache.SigningKey?.Id != version)
            cache.SigningKey = new VaultSigningKey(version, cache.Client.GetCryptographyClient(reference.Name, version));

        cache.RefreshAt = clock.GetUtcNow() + VersionRefreshInterval;
        return cache.SigningKey;
    }

    private static VaultKeyReference? ParseVaultReference(MerchantBillingDelegationOptions settings, string? ordinaryKey)
    {
        if (!string.IsNullOrEmpty(settings.LocalPrivateKeyPem) ||
            !Uri.TryCreate(settings.KeyVaultKeyId, UriKind.Absolute, out var uri) || !uri.IsWellFormedOriginalString() ||
            uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            return null;

        var segments = uri.AbsolutePath.Split('/');
        if (segments.Length is < 3 or > 4 || segments[1] != "keys" || !MerchantBillingDelegationV1.IsKeyId(segments[2]))
            return null;
        var name = segments[2];
        var version = segments.Length == 4 ? segments[3] : null;
        if ((version is not null && !MerchantBillingDelegationV1.IsKeyId(version)) || SharesJwtKey(uri, name, ordinaryKey))
            return null;

        return new VaultKeyReference(new Uri(uri.GetLeftPart(UriPartial.Authority)), name, version, settings.ManagedIdentityClientId);
    }

    private static bool SharesJwtKey(Uri billingUri, string billingName, string? ordinaryKey)
    {
        if (!Uri.TryCreate(ordinaryKey, UriKind.Absolute, out var jwtUri) || jwtUri.Port != billingUri.Port ||
            !string.Equals(jwtUri.IdnHost.TrimEnd('.'), billingUri.IdnHost.TrimEnd('.'), StringComparison.OrdinalIgnoreCase))
            return false;

        var segments = jwtUri.AbsolutePath.Trim('/').Split('/');
        return segments.Length >= 2 &&
            string.Equals(segments[0], "keys", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(segments[1], billingName, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => _vaultGate.Dispose();

    private sealed record VaultKeyReference(Uri Vault, string Name, string? Version, string? ManagedIdentityClientId);

    private sealed class VaultKeyCache(VaultKeyReference reference, KeyClient client)
    {
        internal VaultKeyReference Reference { get; } = reference;
        internal KeyClient Client { get; } = client;
        internal VaultSigningKey? SigningKey { get; set; }
        internal DateTimeOffset RefreshAt { get; set; }
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
