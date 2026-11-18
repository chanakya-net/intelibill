using Intelibill.Application.Common.Licensing;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Licensing;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Intelibill.Application.Features.Billing;
using Intelibill.Infrastructure.Options;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Intelibill.Infrastructure.Billing;

internal static class MerchantBillingRegistration
{
    internal const string ClientName = "MerchantBillingDelegation";

    internal static void AddMerchantBillingDelegation(this IServiceCollection services)
    {
        services.AddOptions<MerchantBillingDelegationOptions>().BindConfiguration(MerchantBillingDelegationOptions.Section);
        services.AddSingleton<IMerchantBillingSigningKeys, MerchantBillingDelegationKeys>();
        services.AddScoped<MerchantBillingDelegationIssuer>();
        services.AddScoped<ShopAffiliationConsentStore>();
        AddDeliveryKey(services, ShopAffiliationDeliveryV1.Section);
        AddDeliveryKey(services, ShopTrialsV1.DeliverySection);
        AddDeliveryKey(services, EntitlementSnapshotsV1.Section);
        services.AddOptions<EntitlementTransportOptions>().BindConfiguration(EntitlementSnapshotsV1.Section);
        services.AddScoped<MerchantEntitlementTransport>();
        services.AddSingleton<ILicenceActionPolicy, LicenceActionPolicy>();
        services.AddScoped<ShopLicenceFence>();
        services.AddScoped<IShopLicenceFence>(provider => provider.GetRequiredService<ShopLicenceFence>());
        services.AddScoped<EntitlementProjectionStore>();
        services.AddScoped<IShopTradingAuthorityReader, ShopTradingAuthorityReader>();
        services.AddScoped<ISuspensionAccessReader, SuspensionAccessReader>();
        services.AddScoped(provider => new LeasedHttpOutbox(provider.GetRequiredService<ApplicationDbContext>()));
        services.AddScoped<EntitlementAcknowledgementMetrics>();
        services.AddScoped<ILeasedHttpDestination, MerchantEntitlementDelivery>();
        LeasedHttpDeliveryPump.Register(services);
        services.TryAddKeyedSingleton<TimeProvider>(ShopTrialsV1.DeliverySection,
            (provider, _) => provider.GetRequiredService<TimeProvider>());
        services.AddScoped<TrialActivationConsentStore>();
        services.AddScoped<TrialActivationDelivery>();
        services.AddHostedService<TrialActivationDeliveryWorker>();
        services.AddScoped<ShopAffiliationDelivery>();
        services.AddHostedService<ShopAffiliationDeliveryWorker>();
        services.AddHttpClient(ClientName)
            .AddTypedClient<IMerchantBillingAccess, MerchantBillingClient>()
            .AddTypedClient<IMerchantBillingInvitations, MerchantBillingClient>()
            .AddTypedClient<IMerchantShopAffiliations, MerchantBillingClient>()
            .AddTypedClient<IMerchantBillingOwnershipTransfers, MerchantBillingClient>()
            .AddTypedClient<IMerchantSubscriptionStatus, MerchantBillingClient>()
            .AddTypedClient<IMerchantSubscriptionCancellations, MerchantBillingClient>()
            .AddTypedClient<IMerchantShopTrials, MerchantBillingClient>()
            .AddTypedClient<IMerchantCheckoutOrders, MerchantBillingClient>()
            .AddTypedClient<IMerchantCheckoutPayments, MerchantBillingClient>()
            .AddTypedClient<IMerchantPaymentConfirmations, MerchantBillingClient>()
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseProxy = false,
                MaxResponseHeadersLength = 8,
                ActivityHeadersPropagator = null,
            });
    }

    private static void AddDeliveryKey(IServiceCollection services, string section)
    {
        services.AddOptions<MerchantBillingDelegationOptions>(section).BindConfiguration(section);
        services.AddKeyedSingleton<IMerchantBillingSigningKeys>(section, (provider, _) =>
            MerchantBillingDelegationKeys.ForDelivery(
                provider.GetRequiredService<IOptionsMonitor<MerchantBillingDelegationOptions>>(),
                provider.GetRequiredService<IOptions<JwtOptions>>(), provider.GetRequiredService<IHostEnvironment>(),
                provider.GetRequiredService<TimeProvider>(), section));
    }
}
