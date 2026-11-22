using System.Security.Cryptography;
using System.Text;
using Intelibill.Application.Common.Licensing.Offline;
using Intelibill.Infrastructure.Billing;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.Options;

namespace Intelibill.Infrastructure.Licensing;

/// <summary>Public half pinned for a vault-held permit key: the exact key version and its SPKI in PEM.</summary>
internal sealed class OfflinePermitKeyOptions
{
    public string? PublicKeyId { get; init; }
    public string? PublicKeyPem { get; init; }
}

/// <summary>
/// The offline-permit purpose of the merchant's named signing keys. The private key stays where the
/// existing facility keeps it (local settings in Development/Testing, otherwise Key Vault); this
/// adapter adds the public half for the exact key identifier and proves each signature against it,
/// so only verifiable evidence with archivable public material ever leaves.
/// </summary>
internal sealed class OfflinePermitSigningKeys(
    IMerchantBillingSigningKeys keys, IOptionsMonitor<MerchantBillingDelegationOptions> signing,
    IOptionsMonitor<OfflinePermitKeyOptions> pins) : IOfflinePermitSigner
{
    private static readonly TimeSpan SignBudget = TimeSpan.FromSeconds(5);

    // Every other purpose of the same facility. A key that signs requests must never sign permits.
    private static readonly string[] OtherPurposes =
    [
        Microsoft.Extensions.Options.Options.DefaultName, ShopAffiliationDeliveryV1.Section, ShopTrialsV1.DeliverySection, EntitlementSnapshotsV1.Section,
    ];

    public async Task<OfflinePermitSignature?> SignAsync(string type, byte[] payload, CancellationToken ct)
    {
        var settings = signing.Get(OfflinePermitContractV1.Section);
        if (OtherPurposes.Any(purpose => SharesKey(settings, signing.Get(purpose))))
            return null;

        using var key = await keys.ResolveAsync(ct);
        if (key is null || PublicKeyOf(settings, key.Id) is not { } publicKey)
            return null;

        var input = OfflinePermitSignatures.SigningInput(type, key.Id, payload);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(SignBudget);
        byte[] signature;
        try
        {
            signature = await key.SignAsync(Encoding.ASCII.GetBytes(input), budget.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The caller holds row and advisory locks while this waits; give up rather than hold them.
            return null;
        }

        var token = OfflinePermitSignatures.Attach(input, signature);
        return OfflinePermitSignatures.Verify(token, type, publicKey).IsError ? null : new(key.Id, publicKey, token);
    }

    private byte[]? PublicKeyOf(MerchantBillingDelegationOptions settings, string keyId)
    {
        if (settings.LocalPrivateKeyPem is { Length: > 0 } local)
            return ExportPublic(local);

        var pin = pins.CurrentValue;
        return pin.PublicKeyId == keyId && pin.PublicKeyPem is { Length: > 0 } pem ? ExportPublic(pem) : null;
    }

    private static bool SharesKey(MerchantBillingDelegationOptions permit, MerchantBillingDelegationOptions other)
    {
        if (permit.LocalPrivateKeyPem is { Length: > 0 } mine && other.LocalPrivateKeyPem is { Length: > 0 } theirs)
            return ExportPublic(mine) is { } left && ExportPublic(theirs) is { } right && left.AsSpan().SequenceEqual(right);
        return VaultKey(permit.KeyVaultKeyId) is { } key && key == VaultKey(other.KeyVaultKeyId);
    }

    /// <summary>Vault and key name without the version: two versions of one key are still one key.</summary>
    private static string? VaultKey(string? reference)
    {
        if (!Uri.TryCreate(reference, UriKind.Absolute, out var uri))
            return null;
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        return segments.Length >= 2 ? $"{uri.IdnHost.TrimEnd('.')}:{uri.Port}/{segments[1]}".ToUpperInvariant() : null;
    }

    private static byte[]? ExportPublic(string pem)
    {
        using var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
            return rsa.KeySize >= OfflinePermitSignatures.MinimumKeyBits ? rsa.ExportSubjectPublicKeyInfo() : null;
        }
        catch (Exception error) when (error is ArgumentException or CryptographicException)
        {
            return null;
        }
    }
}
