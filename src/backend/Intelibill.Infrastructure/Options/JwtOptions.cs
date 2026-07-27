using System.ComponentModel.DataAnnotations;

namespace Intelibill.Infrastructure.Options;

public enum JwtSigningMode
{
    /// <summary>
    /// HS256 with a shared secret. Local development only: the same value both
    /// signs and verifies, so whatever holds it can mint a token for any user in
    /// any shop.
    /// </summary>
    Hmac,

    /// <summary>
    /// RS256 with a key generated inside Key Vault. The private key has no export
    /// operation, so the application can ask for a signature but never holds the
    /// means to produce one elsewhere.
    /// </summary>
    KeyVault,
}

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// HMAC signing key. Unset under <see cref="JwtSigningMode.KeyVault"/>, where
    /// no shared secret exists — see <see cref="JwtOptionsValidator"/>.
    /// </summary>
    public string? Secret { get; init; }

    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    [Range(1, 1440)]
    public int AccessTokenExpiryMinutes { get; init; } = 15;

    [Range(1, 365)]
    public int RefreshTokenExpiryDays { get; init; } = 7;

    /// <summary>
    /// Defaults to HMAC so a developer machine and the test suite need no Azure
    /// dependency at all. Deployed environments set KeyVault.
    /// </summary>
    public JwtSigningMode SigningMode { get; init; } = JwtSigningMode.Hmac;

    /// <summary>
    /// Versionless key identifier, for example
    /// <c>https://intelibill-prod-kv.vault.azure.net/keys/jwt-signing</c>.
    /// Versionless on purpose: the signer resolves whichever version is current,
    /// so a rotation needs neither a configuration change nor a redeploy.
    /// </summary>
    public string? KeyVaultKeyId { get; init; }

    /// <summary>
    /// Client ID of the user-assigned managed identity used to reach Key Vault.
    /// Only needed when the host does not already carry <c>AZURE_CLIENT_ID</c>.
    /// </summary>
    public string? ManagedIdentityClientId { get; init; }

    /// <summary>
    /// How long a resolved key version is reused before the signer looks for a
    /// newer one — the bound on how long tokens keep being signed with the old
    /// version after Key Vault rotates.
    /// </summary>
    public TimeSpan SigningKeyRefreshInterval { get; init; } = TimeSpan.FromMinutes(10);
}
