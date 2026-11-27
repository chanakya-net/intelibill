using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Intelibill.Licensing.Infrastructure.Entitlements;

public sealed class LicensingEntitlementTransport(IOptionsMonitor<EntitlementTransportOptions> options,
    IConfiguration configuration, IHostEnvironment environment, TimeProvider clock)
{
    private static readonly string[] OtherPurposes =
        ["MerchantBillingVerification", ShopAffiliationDeliveryV1.Section, ShopTrialsV1.DeliverySection];

    private bool Local => environment.IsDevelopment() || environment.IsEnvironment("Testing");
    internal Uri? Origin => options.CurrentValue.Origin(Local);

    private IEnumerable<string?> OtherKeys => OtherPurposes
        .SelectMany(purpose => configuration.GetSection($"{purpose}:PublicKeys").GetChildren().Select(child => child.Value));

    internal bool HasPublicationRoute()
    {
        var settings = options.CurrentValue;
        return settings.Origin(Local) is not null && (settings.KeyVaultKeyId is null
            ? settings.CanSignLocally(Local, OtherKeys) : ReadVaultKeyId(settings) is not null);
    }

    public VerifiedServiceAssertion? Verify(string path, string token, byte[] body) =>
        SignedServiceAssertion.Verify(token, SignedServiceAssertion.MerchantEvents, path, body, clock,
            key => options.CurrentValue.VerificationKey(key, OtherKeys));

    internal async Task<string?> SignAsync(byte[] body, CancellationToken ct, string path = EntitlementSnapshotsV1.SnapshotPath)
    {
        var settings = options.CurrentValue;
        if (settings.KeyVaultKeyId is null)
            return await settings.TrySignLocalAsync(SignedServiceAssertion.LicensingDelivery, path,
                body, clock, Local, OtherKeys, ct);
        return await SignWithVaultAsync(settings, body, path, ct);
    }

    private async Task<string?> SignWithVaultAsync(EntitlementTransportOptions settings, byte[] body, string path, CancellationToken ct)
    {
        if (ReadVaultKeyId(settings) is not { } keyId)
            return null;
        var (id, name, version) = keyId;

        var identity = Guid.TryParse(settings.ManagedIdentityClientId ?? configuration["AZURE_CLIENT_ID"], out var clientId)
            ? ManagedIdentityId.FromUserAssignedClientId(clientId.ToString("D"))
            : ManagedIdentityId.SystemAssigned;
        var keys = new KeyClient(new Uri(id.GetLeftPart(UriPartial.Authority)), new ManagedIdentityCredential(identity));
        var key = (await keys.GetKeyAsync(name, version, ct)).Value;
        using var rsa = key.Key.ToRSA();
        if (key.Properties.Enabled is not true || rsa.KeySize is < 2048 or > 4096 ||
            !EntitlementTransportOptions.IsDistinct(rsa.ExportSubjectPublicKeyInfoPem(),
                OtherKeys.Concat(settings.PublicKeys.Values).Concat(settings.RetiredPublicKeys.Values)))
            return null;
        var client = keys.GetCryptographyClient(name, version);
        return await SignedServiceAssertion.SignAsync(SignedServiceAssertion.LicensingDelivery, version,
            path, body, clock, async (input, token) =>
                (await client.SignDataAsync(SignatureAlgorithm.RS256, input, token)).Signature, ct);
    }

    private static (Uri Uri, string Name, string Version)? ReadVaultKeyId(EntitlementTransportOptions settings) =>
        settings.LocalPrivateKeyPem is null && Uri.TryCreate(settings.KeyVaultKeyId, UriKind.Absolute, out var id) &&
        id.IsWellFormedOriginalString() && id.Scheme == Uri.UriSchemeHttps && id.UserInfo.Length == 0 &&
        id.Query.Length == 0 && id.Fragment.Length == 0 && id.IsDefaultPort &&
        id.Host.EndsWith(".vault.azure.net", StringComparison.OrdinalIgnoreCase) &&
        id.AbsolutePath.Split('/') is ["", "keys", var name, var version] &&
        MerchantBillingDelegationV1.IsKeyId(name) && MerchantBillingDelegationV1.IsKeyId(version)
            ? (id, name, version) : null;
}
