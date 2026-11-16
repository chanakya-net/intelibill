using Intelibill.Licensing.Application.Payments;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Intelibill.Licensing.Infrastructure.Payments;

internal static class CheckoutRegistration
{
    internal static void AddCheckout(this IServiceCollection services)
    {
        services.TryAddKeyedSingleton<TimeProvider>(CheckoutOrdersV1.Policy,
            (provider, _) => provider.GetRequiredService<TimeProvider>());
        services.AddScoped<ICheckoutOrderStore, CheckoutOrderStore>();
        services.AddScoped<ReadCheckoutOrdersHandler>();
        services.AddScoped(provider => new AcceptCheckoutOrderHandler(
            provider.GetRequiredService<Application.Billing.IBillingDelegationAccess>(),
            provider.GetRequiredService<ICheckoutOrderStore>(),
            provider.GetRequiredKeyedService<TimeProvider>(CheckoutOrdersV1.Policy)));
        services.AddScoped(provider => new CreateCheckoutOrderHandler(
            provider.GetRequiredService<Application.Billing.IBillingDelegationAccess>(),
            provider.GetRequiredService<ICheckoutOrderStore>(),
            provider.GetRequiredService<Application.Catalogue.IPublishedCatalogueReader>(),
            provider.GetRequiredService<Application.Catalogue.ICataloguePublicationApproval>(),
            provider.GetRequiredKeyedService<TimeProvider>(CheckoutOrdersV1.Policy)));
    }
}
