using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Infrastructure.Payments.Development;
using Intelibill.Licensing.Infrastructure.Payments.Razorpay;
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
        services.AddCapacityChangeQuotes();
    }

    // Capacity previews share the checkout clock and quote lifetime, and the provider selection of new attempts.
    private static void AddCapacityChangeQuotes(this IServiceCollection services)
    {
        services.AddScoped<Application.Subscriptions.ICapacityChangeQuoteStore, Subscriptions.CapacityChangeQuoteStore>();
        services.AddScoped<Application.Subscriptions.ReadCapacityChangeShopsHandler>();
        services.AddScoped(provider => ActivatorUtilities.CreateInstance<Application.Subscriptions.CreateCapacityChangeQuoteHandler>(
            provider, CheckoutClock(provider)));
    }

    private static void AddPayments(this IServiceCollection services)
    {
        services.AddOptions<PaymentOptions>().BindConfiguration(PaymentOptions.Section);
        services.AddOptions<DevelopmentWebhookOptions>().BindConfiguration("Payments:Simulator");
        services.AddOptions<RazorpayOptions>().BindConfiguration(RazorpayOptions.Section);
        services.AddHttpClient(RazorpayGateway.ClientName).RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddScoped<PaymentStore>();
        services.AddScoped<IPaymentConfirmationStore>(p => p.GetRequiredService<PaymentStore>());
        services.AddScoped<ReadPaymentConfirmationHandler>();
        services.AddScoped<IPaymentStore>(provider => provider.GetRequiredService<PaymentStore>());
        services.AddScoped<IPaymentReviewDecisionStore>(provider => provider.GetRequiredService<PaymentStore>());
        services.AddScoped(provider => ActivatorUtilities.CreateInstance<ReviewUnmatchedPaymentHandler>(provider, CheckoutClock(provider)));
        services.AddScoped<IPaymentReviewStore>(provider => provider.GetRequiredService<PaymentStore>());
        services.AddScoped<IManualPaymentStore>(provider => provider.GetRequiredService<PaymentStore>());
        services.AddOptions<ManualReceivingAccountOptions>().BindConfiguration("ManualPayments");
        services.AddScoped<IManualPaymentEvidence, ManualPaymentEvidenceProtector>();
        services.AddScoped<FluentValidation.IValidator<ConfirmManualPayment>, ConfirmManualPaymentValidator>();
        services.AddScoped(provider => ActivatorUtilities.CreateInstance<ConfirmManualPaymentHandler>(provider, CheckoutClock(provider)));
        services.AddScoped<IGatewayInboxStore, GatewayInboxStore>();
        services.AddScoped<IGatewayInboxEvidence, GatewayInboxEvidenceProtector>();
        services.AddScoped(provider => ActivatorUtilities.CreateInstance<ReceiveGatewayWebhookHandler>(provider,
            CheckoutClock(provider)));
        services.AddScoped<IPendingPaymentAttemptStore, PendingPaymentAttemptStore>();
        services.TryAddKeyedSingleton<TimeProvider>(PendingPaymentAttemptWorker.ClockKey, (_, _) => TimeProvider.System);
        services.AddHostedService<PendingPaymentAttemptWorker>();
        services.AddHostedService<GatewayInboxWorker>();
        services.AddScoped(provider => ActivatorUtilities.CreateInstance<ProcessGatewayInboxHandler>(provider,
            provider.GetRequiredKeyedService<TimeProvider>(PendingPaymentAttemptWorker.ClockKey)));
        services.AddScoped(provider => new DevelopmentPaymentGateway(
            provider.GetRequiredService<Data.LicensingDbContext>(), CheckoutClock(provider),
            provider.GetRequiredService<IOptions<DevelopmentWebhookOptions>>().Value));
        services.AddScoped<PaymentProviderConfiguration>();
        services.AddScoped<PaymentProviderBindings>();
        services.AddScoped<PaymentGatewayResolver>();
        services.AddScoped<IPaymentGatewayResolver>(p => p.GetRequiredService<PaymentGatewayResolver>());
        services.AddScoped<IPaymentProviderRegistry>(p => p.GetRequiredService<PaymentGatewayResolver>());
        services.AddScoped<PaymentProviderRoutingStore>();
        services.AddScoped<IPaymentProviderRoutingStore>(p => p.GetRequiredService<PaymentProviderRoutingStore>());
        services.AddScoped<IPaymentProviderSelection>(p => p.GetRequiredService<PaymentProviderRoutingStore>());
        services.AddScoped<SelectPaymentProviderHandler>();
        services.AddScoped<IPaymentSimulator>(provider =>
            PaymentGatewayResolver.SimulatorEnabled(provider.GetRequiredService<IOptions<PaymentOptions>>().Value,
                provider.GetRequiredService<IHostEnvironment>())
                ? provider.GetRequiredService<DevelopmentPaymentGateway>()
                : throw new InvalidOperationException("The payment simulator is local-only."));
        services.AddScoped(provider => new CheckoutPaymentStatusReader(provider.GetRequiredService<ICheckoutOrderStore>(),
            provider.GetRequiredService<IPaymentStore>(),
            provider.GetRequiredService<Application.Status.IEntitlementSynchronizationReader>(),
            provider.GetRequiredService<IPaymentGatewayResolver>(), CheckoutClock(provider),
            provider.GetRequiredService<IPaymentProviderSelection>(),
            provider.GetRequiredService<Application.Restrictions.IClosureStore>()));
        services.AddScoped(provider => new FulfilPaidOrderHandler(provider.GetRequiredService<IPaymentStore>(),
            provider.GetRequiredService<ICheckoutOrderStore>(), provider.GetRequiredService<IPaymentReviewStore>(),
            provider.GetRequiredService<IPaymentConfirmationStore>(),
            provider.GetRequiredKeyedService<TimeProvider>(SubscriptionStatusRegistration.ClockKey)));
        services.AddScoped(provider => new ReconcilePaymentHandler(provider.GetRequiredService<IPaymentStore>(),
            provider.GetRequiredService<IPaymentGatewayResolver>(), provider.GetRequiredService<FulfilPaidOrderHandler>(),
            provider.GetRequiredService<IPendingPaymentAttemptStore>(), provider.GetRequiredService<IPaymentReviewStore>(), CheckoutClock(provider),
            provider.GetRequiredKeyedService<TimeProvider>(PendingPaymentAttemptWorker.ClockKey)));
        services.AddScoped(provider => ActivatorUtilities.CreateInstance<StartCheckoutPaymentHandler>(provider, CheckoutClock(provider)));
        services.AddScoped(provider => ActivatorUtilities.CreateInstance<VerifyCheckoutPaymentHandler>(provider, CheckoutClock(provider)));
        services.AddScoped<ReadCheckoutPaymentHandler>();
        services.AddScoped(provider => ActivatorUtilities.CreateInstance<SimulateCheckoutCaptureHandler>(provider, CheckoutClock(provider)));
    }

    private static TimeProvider CheckoutClock(IServiceProvider provider) =>
        provider.GetRequiredKeyedService<TimeProvider>(CheckoutOrdersV1.Policy);
}
