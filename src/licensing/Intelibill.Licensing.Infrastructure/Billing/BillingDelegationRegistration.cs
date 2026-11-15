using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.BillingAccounts;
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
        services.AddScoped<IBillingAccountStore, BillingAccountStore>();
        // Registered with their ports: the bootstrap CLI host validates its graph without billing.
        services.AddScoped<CreateBillingAccountHandler>();
        services.AddScoped<ListBillingAccountsHandler>();
        services.TryAddScoped<IBillingAccountDelegationAuthority, BillingAccountMembershipAuthority>();
    }
}
