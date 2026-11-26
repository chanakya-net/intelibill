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

    /// <summary>
    /// How long one delegated call to licensing may take. Values outside (0, 2 minutes] fall back to the
    /// five-second default; integration hosts raise it so a loaded test machine is not mistaken for an outage.
    /// </summary>
    public TimeSpan RequestTimeout { get; init; } = DefaultRequestTimeout;

    internal static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(5);

    internal TimeSpan EffectiveRequestTimeout =>
        RequestTimeout > TimeSpan.Zero && RequestTimeout <= TimeSpan.FromMinutes(2) ? RequestTimeout : DefaultRequestTimeout;

    internal static bool AllowsLocalCredentials(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Testing");

    internal Uri? GetOrigin(IHostEnvironment environment) =>
        Uri.TryCreate(LicensingOrigin, UriKind.Absolute, out var uri) &&
        uri.AbsolutePath == "/" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
        (uri.Scheme == Uri.UriSchemeHttps ||
            (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback && AllowsLocalCredentials(environment)))
            ? uri : null;
}
