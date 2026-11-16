using Intelibill.Licensing.Application.Entitlements;
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
        services.AddScoped<IEntitlementPublisher>(provider => provider.GetRequiredService<EntitlementPublisher>());
        services.AddScoped<LicensingEntitlementTransport>();
        services.AddScoped(provider => new EntitlementMerchantEvents(provider.GetRequiredService<LicensingDbContext>(),
            provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<EntitlementPublisher>()));
        services.AddScoped(provider => new LeasedHttpOutbox(provider.GetRequiredService<LicensingDbContext>()));
        services.AddScoped<ILeasedHttpDestination, LicensingEntitlementDelivery>();
        LeasedHttpDeliveryPump.Register(services);
    }
}
