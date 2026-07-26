using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Intelibill.Infrastructure.Services.Auth;

/// <summary>
/// RS256 with a key that lives in Key Vault. The private key is not readable by
/// anything, including this process: signing is a vault operation, so a
/// compromised container can ask for signatures while its access lasts, rather
/// than walk away with the ability to mint tokens forever.
/// </summary>
internal sealed class KeyVaultJwtSigner : IJwtSigner, IDisposable
{
    private readonly KeyClient _keyClient;
    private readonly string _keyName;
    private readonly TimeSpan _refreshInterval;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private CryptographyClient? _cryptographyClient;
    private string? _keyId;
    private DateTimeOffset _resolvedAt;

    public KeyVaultJwtSigner(IOptions<JwtOptions> options, TimeProvider timeProvider)
    {
        var jwt = options.Value;

        var keyId = jwt.KeyVaultKeyId
            ?? throw new InvalidOperationException("Jwt:KeyVaultKeyId is required for Key Vault signing.");

        // Versionless: ".../keys/<name>". Splitting it back into vault URI and key
        // name is what lets the signer follow a rotation without being told.
        var uri = new Uri(keyId);
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 2 || !string.Equals(segments[0], "keys", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Jwt:KeyVaultKeyId must look like https://<vault>.vault.azure.net/keys/<name>, but was '{keyId}'.");
        }

        _keyName = segments[1];
        _keyClient = new KeyClient(
            new Uri(uri.GetLeftPart(UriPartial.Authority)),
            AzureCredentials.Create(jwt.ManagedIdentityClientId));

        _refreshInterval = jwt.SigningKeyRefreshInterval;
        _timeProvider = timeProvider;
    }

    public string Algorithm => "RS256";

    public async ValueTask<string?> GetKeyIdAsync(CancellationToken cancellationToken = default)
        => (await ResolveAsync(cancellationToken).ConfigureAwait(false)).KeyId;

    public async ValueTask<byte[]> SignAsync(byte[] signingInput, CancellationToken cancellationToken = default)
    {
        var (client, _) = await ResolveAsync(cancellationToken).ConfigureAwait(false);

        // SignData hashes for us, and binds the signature to the exact key version
        // the client was built from — the same version GetKeyIdAsync advertised, so
        // the kid in the header always matches what actually signed.
        var result = await client
            .SignDataAsync(SignatureAlgorithm.RS256, signingInput, cancellationToken)
            .ConfigureAwait(false);

        return result.Signature;
    }

    private async ValueTask<(CryptographyClient Client, string KeyId)> ResolveAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        if (_cryptographyClient is not null && _keyId is not null && now - _resolvedAt < _refreshInterval)
        {
            return (_cryptographyClient, _keyId);
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = _timeProvider.GetUtcNow();
            if (_cryptographyClient is not null && _keyId is not null && now - _resolvedAt < _refreshInterval)
            {
                return (_cryptographyClient, _keyId);
            }

            // No version in the request: Key Vault returns the current one, which is
            // how a rotation is picked up without redeploying.
            var key = await _keyClient.GetKeyAsync(_keyName, cancellationToken: cancellationToken).ConfigureAwait(false);

            _cryptographyClient = _keyClient.GetCryptographyClient(_keyName, key.Value.Properties.Version);
            _keyId = key.Value.Properties.Version;
            _resolvedAt = now;

            return (_cryptographyClient, _keyId);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Dispose() => _refreshLock.Dispose();
}
