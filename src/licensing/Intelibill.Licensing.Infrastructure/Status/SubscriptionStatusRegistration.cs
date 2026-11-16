using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Entitlements;
using Intelibill.Licensing.Application.Subscriptions;
using Intelibill.Licensing.Infrastructure.Subscriptions;
using Intelibill.Licensing.Application.Status;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Intelibill.Licensing.Infrastructure.Status;

internal static class SubscriptionStatusRegistration
{
    /// <summary>
    /// The clock status evaluations read once per response. It is the application clock; only a test host
    /// replaces this keyed registration to evaluate a fixed instant. No endpoint can change it.
    /// </summary>
    internal const string ClockKey = "subscription-status";

    internal static void AddSubscriptionStatus(this IServiceCollection services)
    {
        services.TryAddKeyedSingleton<TimeProvider>(ClockKey, (provider, _) => provider.GetRequiredService<TimeProvider>());
        services.AddScoped<ISubscriptionStatusStore, SubscriptionStatusStore>();
        services.AddScoped<SubscriptionAccessFactsReader>();
        services.AddScoped<ISubscriptionAccessFactsReader>(provider => provider.GetRequiredService<SubscriptionAccessFactsReader>());
        services.AddScoped<IScheduledSubscriptionChangeStore, ScheduledSubscriptionChangeStore>();
        services.AddScoped(provider => new ScheduleCancellationHandler(
            provider.GetRequiredService<IBillingDelegationAccess>(), provider.GetRequiredService<IScheduledSubscriptionChangeStore>(),
            provider.GetRequiredService<IEntitlementPublisher>(), provider.GetRequiredKeyedService<TimeProvider>(ClockKey)));
        services.AddScoped(provider => new ReadSubscriptionStatusHandler(
            provider.GetRequiredService<IBillingDelegationAccess>(), provider.GetRequiredService<ISubscriptionStatusStore>(),
            provider.GetRequiredService<ISubscriptionAccessFactsReader>(), provider.GetRequiredKeyedService<TimeProvider>(ClockKey),
            provider.GetRequiredService<IEntitlementSynchronizationReader>()));
        services.AddScoped(provider => new ReadProviderStatusHandler(
            provider.GetRequiredService<ISubscriptionStatusStore>(), provider.GetRequiredService<ISubscriptionAccessFactsReader>(),
            provider.GetRequiredKeyedService<TimeProvider>(ClockKey), provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IEntitlementSynchronizationReader>(), provider.GetRequiredService<ScheduleCancellationHandler>()));
    }
}
