using Intelibill.Infrastructure.Billing;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Intelibill.Infrastructure.Licensing;

public sealed class MerchantEntitlementTransport(IOptionsMonitor<EntitlementTransportOptions> options,
    IConfiguration configuration, IHostEnvironment environment, TimeProvider clock, IServiceProvider services)
{
    private static readonly string[] OtherPurposes =
        [MerchantBillingDelegationOptions.Section, ShopAffiliationDeliveryV1.Section, ShopTrialsV1.DeliverySection, "Jwt"];

    private bool Local => MerchantBillingDelegationOptions.AllowsLocalCredentials(environment);
    internal Uri? Origin => options.CurrentValue.Origin(Local);
    private IEnumerable<string?> OtherKeys => OtherPurposes.Select(purpose => configuration[$"{purpose}:LocalPrivateKeyPem"]);

    public VerifiedServiceAssertion? Verify(string token, byte[] body, string path = EntitlementSnapshotsV1.SnapshotPath) =>
        SignedServiceAssertion.Verify(token, SignedServiceAssertion.LicensingDelivery, path,
            body, clock, key => options.CurrentValue.VerificationKey(key, OtherKeys));

    public Task<string?> SignClosureValidationAsync(byte[] body, CancellationToken ct) =>
        SignAsync(ShopClosuresV1.ValidationPath, body, ct);

    internal async Task<string?> SignAsync(string path, byte[] body, CancellationToken ct)
    {
        var settings = options.CurrentValue;
        if (settings.KeyVaultKeyId is null)
            return await settings.TrySignLocalAsync(SignedServiceAssertion.MerchantEvents, path, body, clock, Local, OtherKeys, ct);

        if (OtherPurposes.Any(purpose => SameVaultKey(settings.KeyVaultKeyId, configuration[$"{purpose}:KeyVaultKeyId"])))
            return null;
        var keys = services.GetRequiredKeyedService<IMerchantBillingSigningKeys>(EntitlementSnapshotsV1.Section);
        using var key = await keys.ResolveAsync(ct);
        return key is null ? null : await SignedServiceAssertion.SignAsync(SignedServiceAssertion.MerchantEvents,
            key.Id, path, body, clock, key.SignAsync, ct);
    }

    private static bool SameVaultKey(string first, string? second) =>
        VaultName(first) is { } key && string.Equals(key, VaultName(second), StringComparison.OrdinalIgnoreCase);

    private static string? VaultName(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Segments.Length >= 3
            ? uri.GetLeftPart(UriPartial.Authority) + string.Concat(uri.Segments.Skip(1).Take(2)).TrimEnd('/')
            : null;
}
