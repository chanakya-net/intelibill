using Microsoft.Extensions.Hosting;

namespace Intelibill.Infrastructure.Billing;

internal sealed class MerchantBillingDelegationOptions
{
    internal const string Section = "MerchantBillingDelegation";

    public string? LicensingOrigin { get; init; }
    public string? LocalKeyId { get; init; }
    public string? LocalPrivateKeyPem { get; init; }
    public string? KeyVaultKeyId { get; init; }
    public string? ManagedIdentityClientId { get; init; }

    internal static bool AllowsLocalCredentials(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Testing");

    internal Uri? GetOrigin(IHostEnvironment environment) =>
        Uri.TryCreate(LicensingOrigin, UriKind.Absolute, out var uri) &&
        uri.AbsolutePath == "/" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
        (uri.Scheme == Uri.UriSchemeHttps ||
            (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback && AllowsLocalCredentials(environment)))
            ? uri : null;
}
