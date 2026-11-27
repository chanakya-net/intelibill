using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Trials;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Infrastructure.Billing;
using Intelibill.Licensing.Infrastructure.Status;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Intelibill.Licensing.Infrastructure.Trials;

internal static class TrialRegistration
{
    internal static void AddShopTrials(this IServiceCollection services)
    {
        services.AddOptions<MerchantBillingVerificationOptions>(ShopTrialsV1.DeliverySection)
            .BindConfiguration(ShopTrialsV1.DeliverySection);
        services.AddSingleton<ITrialDeliveryVerifier, MerchantBillingDelegationVerifier>();
        services.TryAddKeyedSingleton<TimeProvider>(ShopTrialsV1.DeliverySection,
            (provider, _) => provider.GetRequiredService<TimeProvider>());
        services.AddScoped<ITrialExtensionStore, TrialExtensionStore>();
        services.AddScoped(provider => new ExtendTrialHandler(
            provider.GetRequiredService<ITrialExtensionStore>(), provider.GetRequiredService<ISubscriptionAccessFactsReader>(),
            provider.GetRequiredKeyedService<TimeProvider>(SubscriptionStatusRegistration.ClockKey),
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<ITrialStore, TrialStore>();
        services.AddScoped<ITrialPolicyReader, TrialPolicyReader>();
        services.AddScoped(provider => new StartTrialHandler(provider.GetRequiredService<ITrialStore>(),
            provider.GetRequiredService<ITrialPolicyReader>(), provider.GetRequiredService<IBillingDelegationAccess>(),
            provider.GetRequiredKeyedService<TimeProvider>(ShopTrialsV1.DeliverySection)));
    }
}
