namespace Intelibill.Licensing.Infrastructure.Billing;

internal sealed class MerchantBillingVerificationOptions
{
    internal const string Section = "MerchantBillingVerification";
    public Dictionary<string, string> PublicKeys { get; init; } = new(StringComparer.Ordinal);
}
