using Intelibill.Licensing.Application.Billing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Intelibill.Licensing.Infrastructure.Billing;

internal static class BillingDelegationRegistration
{
    internal static void AddBillingDelegation(this IServiceCollection services)
    {
        services.AddOptions<MerchantBillingVerificationOptions>().BindConfiguration(MerchantBillingVerificationOptions.Section);
        services.AddSingleton<IMerchantBillingDelegationVerifier, MerchantBillingDelegationVerifier>();
        services.AddScoped<IBillingDelegationAccess, PostgresBillingDelegationAccess>();
        services.TryAddScoped<IBillingAccountDelegationAuthority, UnavailableBillingAccountDelegationAuthority>();
    }
}
