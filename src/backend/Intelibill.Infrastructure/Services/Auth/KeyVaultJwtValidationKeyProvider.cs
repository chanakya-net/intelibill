using Azure.Security.KeyVault.Keys;
using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Intelibill.Infrastructure.Services.Auth;

/// <summary>
/// Supplies the public halves of the Key Vault signing key to token validation.
/// </summary>
/// <remarks>
/// Every enabled version is offered, not only the current one. Key Vault rotation
/// creates a new version and leaves the old one enabled, and tokens signed a
/// minute before the rotation are valid for their full lifetime — dropping the
/// previous version would sign everyone out at the moment of rotation, which is
/// exactly what automatic rotation is supposed to avoid.
/// </remarks>
public sealed class KeyVaultJwtValidationKeyProvider : IDisposable
{
    private readonly KeyClient _keyClient;
    private readonly string _keyName;
    private readonly TimeSpan _refreshInterval;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private List<SecurityKey> _keys = [];
    private DateTimeOffset _refreshedAt = DateTimeOffset.MinValue;

    public KeyVaultJwtValidationKeyProvider(IOptions<JwtOptions> options, TimeProvider timeProvider)
    {
        var jwt = options.Value;

        var keyId = jwt.KeyVaultKeyId
            ?? throw new InvalidOperationException("Jwt:KeyVaultKeyId is required for Key Vault validation.");

        var uri = new Uri(keyId);
        _keyName = uri.AbsolutePath.Trim('/').Split('/')[1];
        _keyClient = new KeyClient(
            new Uri(uri.GetLeftPart(UriPartial.Authority)),
            AzureCredentials.Create(jwt.ManagedIdentityClientId));

        _refreshInterval = jwt.SigningKeyRefreshInterval;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Resolver for <c>TokenValidationParameters.IssuerSigningKeyResolver</c>. A
    /// <c>kid</c> that is not in the cache forces one refresh, so a token signed
    /// by a version created since the last refresh validates immediately instead
    /// of failing until the cache happens to expire.
    /// </summary>
    public IEnumerable<SecurityKey> Resolve(string token, SecurityToken securityToken, string? kid, TokenValidationParameters parameters)
    {
        var keys = GetKeys(forceRefresh: false);

        if (kid is not null && !keys.Any(key => string.Equals(key.KeyId, kid, StringComparison.Ordinal)))
        {
            keys = GetKeys(forceRefresh: true);
        }

        return keys;
    }

    private List<SecurityKey> GetKeys(bool forceRefresh)
    {
        var now = _timeProvider.GetUtcNow();
        if (!forceRefresh && _keys.Count > 0 && now - _refreshedAt < _refreshInterval)
        {
            return _keys;
        }

        // Blocking, because IssuerSigningKeyResolver has no asynchronous form.
        // Bounded by the refresh interval and by the cache miss above, so it runs
        // on the order of once every few minutes rather than per request.
        _refreshLock.Wait();
        try
        {
            now = _timeProvider.GetUtcNow();
            if (!forceRefresh && _keys.Count > 0 && now - _refreshedAt < _refreshInterval)
            {
                return _keys;
            }

            var keys = new List<SecurityKey>();

            foreach (var properties in _keyClient.GetPropertiesOfKeyVersions(_keyName))
            {
                if (properties.Enabled != true)
                {
                    continue;
                }

                var version = _keyClient.GetKey(_keyName, properties.Version);
                keys.Add(new RsaSecurityKey(version.Value.Key.ToRSA())
                {
                    KeyId = properties.Version,
                });
            }

            _keys = keys;
            _refreshedAt = now;

            return _keys;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Dispose() => _refreshLock.Dispose();
}
