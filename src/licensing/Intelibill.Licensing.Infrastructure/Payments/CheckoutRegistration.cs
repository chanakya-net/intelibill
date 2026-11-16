using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Infrastructure.Payments.Development;
using Intelibill.Licensing.Infrastructure.Status;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

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
            provider.GetRequiredService<ICheckoutOrderStore>(), CheckoutClock(provider)));
        services.AddScoped(provider => new CreateCheckoutOrderHandler(
            provider.GetRequiredService<Application.Billing.IBillingDelegationAccess>(),
            provider.GetRequiredService<ICheckoutOrderStore>(),
            provider.GetRequiredService<Application.Catalogue.IPublishedCatalogueReader>(),
            provider.GetRequiredService<Application.Catalogue.ICataloguePublicationApproval>(), CheckoutClock(provider)));
        services.AddPayments();
    }

    private static void AddPayments(this IServiceCollection services)
    {
        services.AddOptions<PaymentOptions>().BindConfiguration(PaymentOptions.Section);
        services.AddScoped<IPaymentStore, PaymentStore>();
        services.AddScoped<IPendingPaymentAttemptStore, PendingPaymentAttemptStore>();
        services.TryAddKeyedSingleton<TimeProvider>(PendingPaymentAttemptWorker.ClockKey, (_, _) => TimeProvider.System);
        services.AddHostedService<PendingPaymentAttemptWorker>();
        services.AddScoped(provider => new DevelopmentPaymentGateway(
            provider.GetRequiredService<Data.LicensingDbContext>(), CheckoutClock(provider)));
        services.AddScoped<IPaymentGatewayResolver, PaymentGatewayResolver>();
        services.AddScoped<IPaymentSimulator>(provider =>
            PaymentGatewayResolver.SimulatorEnabled(provider.GetRequiredService<IOptions<PaymentOptions>>().Value,
                provider.GetRequiredService<IHostEnvironment>())
                ? provider.GetRequiredService<DevelopmentPaymentGateway>()
                : throw new InvalidOperationException("The payment simulator is local-only."));
        services.AddScoped(provider => new CheckoutPaymentStatusReader(provider.GetRequiredService<ICheckoutOrderStore>(),
            provider.GetRequiredService<IPaymentStore>(),
            provider.GetRequiredService<Application.Status.IEntitlementSynchronizationReader>(),
            provider.GetRequiredService<IPaymentGatewayResolver>(), CheckoutClock(provider)));
        services.AddScoped(provider => new FulfilPaidOrderHandler(provider.GetRequiredService<IPaymentStore>(),
            provider.GetRequiredService<ICheckoutOrderStore>(),
            provider.GetRequiredKeyedService<TimeProvider>(SubscriptionStatusRegistration.ClockKey)));
        services.AddScoped(provider => new ReconcilePaymentHandler(provider.GetRequiredService<IPaymentStore>(),
            provider.GetRequiredService<IPaymentGatewayResolver>(), provider.GetRequiredService<FulfilPaidOrderHandler>(),
            provider.GetRequiredService<IPendingPaymentAttemptStore>(), CheckoutClock(provider),
            provider.GetRequiredKeyedService<TimeProvider>(PendingPaymentAttemptWorker.ClockKey)));
        services.AddScoped(provider => ActivatorUtilities.CreateInstance<StartCheckoutPaymentHandler>(provider, CheckoutClock(provider)));
        services.AddScoped(provider => ActivatorUtilities.CreateInstance<VerifyCheckoutPaymentHandler>(provider, CheckoutClock(provider)));
        services.AddScoped<ReadCheckoutPaymentHandler>();
        services.AddScoped(provider => ActivatorUtilities.CreateInstance<SimulateCheckoutCaptureHandler>(provider, CheckoutClock(provider)));
    }

    private static TimeProvider CheckoutClock(IServiceProvider provider) =>
        provider.GetRequiredKeyedService<TimeProvider>(CheckoutOrdersV1.Policy);
}
