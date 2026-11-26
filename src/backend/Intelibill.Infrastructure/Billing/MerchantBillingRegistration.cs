using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Licensing.Offline;
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
        services.AddScoped<CoverageSlotConsentStore>();
        services.AddScoped<ReplacementConsentStore>();
        services.AddScoped<CoverageExceptionConsentStore>();
        services.AddScoped<CoverageTransferShopConsentStore>();
        services.AddScoped<ICoverageTransferAuthorityReceiver, MerchantCoverageTransferAuthority>();
        services.AddScoped<MerchantClosureConsentStore>();
        services.AddScoped<IMerchantClosureValidationReceiver>(provider => provider.GetRequiredService<MerchantClosureConsentStore>());
        AddDeliveryKey(services, ShopAffiliationDeliveryV1.Section);
        AddDeliveryKey(services, ShopTrialsV1.DeliverySection);
        AddDeliveryKey(services, EntitlementSnapshotsV1.Section);
        services.AddOptions<EntitlementTransportOptions>().BindConfiguration(EntitlementSnapshotsV1.Section);
        services.AddScoped<MerchantEntitlementTransport>();
        services.AddSingleton<ILicenceActionPolicy, LicenceActionPolicy>();
        services.AddScoped<ShopLicenceFence>();
        services.AddScoped<IShopLicenceFence>(provider => provider.GetRequiredService<ShopLicenceFence>());
        services.AddScoped<EntitlementProjectionStore>();
        services.AddScoped<ICapacityReservationReceiver, MerchantCapacityReservationStore>();
        services.AddScoped<IReductionReservationReceiver, MerchantReductionReservationStore>();
        services.AddScoped<ICoverageExceptionReceiver, MerchantCoverageExceptionReceiver>();
        services.AddScoped<IShopTradingAuthorityReader, ShopTradingAuthorityReader>();
        services.AddScoped<ISuspensionAccessReader, SuspensionAccessReader>();
        AddDeliveryKey(services, OfflinePermitContractV1.Section);
        services.AddOptions<OfflinePermitKeyOptions>().BindConfiguration(OfflinePermitContractV1.Section);
        services.AddSingleton<IOfflinePermitSigner>(provider => new OfflinePermitSigningKeys(
            provider.GetRequiredKeyedService<IMerchantBillingSigningKeys>(OfflinePermitContractV1.Section),
            provider.GetRequiredService<IOptionsMonitor<MerchantBillingDelegationOptions>>(),
            provider.GetRequiredService<IOptionsMonitor<OfflinePermitKeyOptions>>()));
        services.AddScoped<OfflineDeviceObservationOutbox>();
        services.AddScoped<IOfflineLicenceEvidenceStore, OfflineLicenceEvidenceStore>();
        services.AddScoped<OfflinePermitGates>();
        services.AddSingleton<OfflineSnapshotCaptureRegistry>();
        services.AddScoped<IOfflineLicencePermitIssuer, OfflineLicencePermitIssuer>();
        services.AddScoped<OfflineSnapshotPermitStreamer>();
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
            .AddTypedClient<IMerchantCoverageSlots, MerchantBillingClient>()
            .AddTypedClient<IMerchantCoverageReplacements, MerchantBillingClient>()
            .AddTypedClient<IMerchantCoverageReplacementExceptions, MerchantBillingClient>()
            .AddTypedClient<IMerchantCoverageTransfers, MerchantBillingClient>()
            .AddTypedClient<IMerchantShopAffiliations, MerchantBillingClient>()
            .AddTypedClient<IMerchantShopClosures, MerchantBillingClient>()
            .AddTypedClient<IMerchantBillingOwnershipTransfers, MerchantBillingClient>()
            .AddTypedClient<IMerchantSubscriptionStatus, MerchantBillingClient>()
            .AddTypedClient<IMerchantSubscriptionCancellations, MerchantBillingClient>()
            .AddTypedClient<IMerchantShopTrials, MerchantBillingClient>()
            .AddTypedClient<IMerchantCheckoutOrders, MerchantBillingClient>()
            .AddTypedClient<IMerchantCapacityChangeQuotes, MerchantBillingClient>()
            .AddTypedClient<IMerchantCapacityReductions, MerchantBillingClient>()
            .AddTypedClient<IMerchantRefundRequests, MerchantBillingClient>()
            .AddTypedClient<IMerchantCheckoutPayments, MerchantBillingClient>()
            .AddTypedClient<IMerchantPaymentConfirmations, MerchantBillingClient>()
            .AddTypedClient<IMerchantBillingDocuments, MerchantBillingClient>()
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
