using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Licensing.Application.Coverage;
using Intelibill.Licensing.Infrastructure.Status;
using Intelibill.Licensing.Infrastructure.Trials;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Infrastructure.Billing;

internal static class BillingDelegationRegistration
{
    internal static void AddBillingDelegation(this IServiceCollection services)
    {
        services.AddOptions<MerchantBillingVerificationOptions>().BindConfiguration(MerchantBillingVerificationOptions.Section);
        services.AddOptions<MerchantBillingVerificationOptions>(ShopAffiliationDeliveryV1.Section)
            .BindConfiguration(ShopAffiliationDeliveryV1.Section);
        services.AddSingleton<IShopAffiliationDeliveryVerifier, MerchantBillingDelegationVerifier>();
        services.AddSingleton<IMerchantBillingDelegationVerifier, MerchantBillingDelegationVerifier>();
        services.AddScoped<IBillingDelegationAccess, PostgresBillingDelegationAccess>();
        services.AddScoped<IBillingAccountStore, BillingAccountStore>();
        services.AddScoped<IShopAffiliationStore, ShopAffiliationStore>();
        services.AddScoped<ShopAffiliationHandlers>();
        services.AddScoped<IBillingInvitationStore, BillingInvitationStore>();
        services.AddScoped<BillingInvitationSecrets>();
        services.AddScoped<BillingInvitationHandlers>();
        services.AddScoped<IBillingOwnershipTransferStore, BillingOwnershipTransferStore>();
        services.AddScoped<TransferBillingOwnershipHandler>();
        services.AddScoped<IBillingMemberRemovalStore, BillingMemberRemovalStore>();
        services.AddScoped<RemoveBillingManagerHandler>();
        // Registered with their ports: the bootstrap CLI host validates its graph without billing.
        services.AddScoped<CreateBillingAccountHandler>();
        services.AddScoped<ListBillingAccountsHandler>();
        services.TryAddScoped<IBillingAccountDelegationAuthority, BillingAccountMembershipAuthority>();
        services.AddSubscriptionStatus();
        services.AddShopTrials();
    }
}
