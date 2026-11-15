using Intelibill.Application.Features.Billing;
using Intelibill.Infrastructure.Options;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddOptions<MerchantBillingDelegationOptions>(ShopAffiliationDeliveryV1.Section)
            .BindConfiguration(ShopAffiliationDeliveryV1.Section);
        services.AddKeyedSingleton<IMerchantBillingSigningKeys>(ShopAffiliationDeliveryV1.Section, (provider, _) =>
            MerchantBillingDelegationKeys.ForAffiliation(
                provider.GetRequiredService<IOptionsMonitor<MerchantBillingDelegationOptions>>(),
                provider.GetRequiredService<IOptions<JwtOptions>>(), provider.GetRequiredService<IHostEnvironment>(),
                provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<ShopAffiliationDelivery>();
        services.AddHostedService<ShopAffiliationDeliveryWorker>();
        services.AddHttpClient(ClientName)
            .AddTypedClient<IMerchantBillingAccess, MerchantBillingClient>()
            .AddTypedClient<IMerchantBillingInvitations, MerchantBillingClient>()
            .AddTypedClient<IMerchantShopAffiliations, MerchantBillingClient>()
            .AddTypedClient<IMerchantBillingOwnershipTransfers, MerchantBillingClient>()
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
}
