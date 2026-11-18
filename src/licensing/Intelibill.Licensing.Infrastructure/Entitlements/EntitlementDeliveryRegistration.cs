using Intelibill.Licensing.Application.Restrictions;
using Intelibill.Licensing.Infrastructure.Restrictions;
using Intelibill.Licensing.Infrastructure.Status;
using Intelibill.Licensing.Application.Entitlements;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Infrastructure.Entitlements;

internal static class EntitlementDeliveryRegistration
{
    internal static void AddEntitlementDelivery(this IServiceCollection services)
    {
        services.AddOptions<EntitlementTransportOptions>().BindConfiguration(EntitlementSnapshotsV1.Section);
        services.AddScoped<EntitlementPublisher>();
        services.AddScoped<ISuspensionStore, SuspensionStore>();
        services.AddScoped(provider => new SuspendAccessHandler(provider.GetRequiredService<ISuspensionStore>(),
            provider.GetRequiredService<ISubscriptionStatusStore>(),
            provider.GetRequiredKeyedService<TimeProvider>(SubscriptionStatusRegistration.ClockKey),
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped(provider => new ReleaseSuspensionHandler(provider.GetRequiredService<ISuspensionStore>(),
            provider.GetRequiredService<SuspendAccessHandler>(), provider.GetRequiredService<ISubscriptionAccessFactsReader>(),
            provider.GetRequiredService<ISubscriptionStatusStore>(),
            provider.GetRequiredKeyedService<TimeProvider>(SubscriptionStatusRegistration.ClockKey)));
        services.AddScoped<IEntitlementPublisher>(provider => provider.GetRequiredService<EntitlementPublisher>());
        services.AddScoped<LicensingEntitlementTransport>();
        services.AddScoped<IEntitlementSynchronizationReader, EntitlementSynchronizationReader>();
        services.AddScoped(provider => new EntitlementMerchantEvents(provider.GetRequiredService<LicensingDbContext>(),
            provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<EntitlementPublisher>()));
        services.AddScoped(provider => new LeasedHttpOutbox(provider.GetRequiredService<LicensingDbContext>()));
        services.AddScoped<ILeasedHttpDestination, LicensingEntitlementDelivery>();
        LeasedHttpDeliveryPump.Register(services);
    }
}
