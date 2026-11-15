using Intelibill.Application.Features.Billing;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Infrastructure.Billing;

internal static class MerchantBillingRegistration
{
    internal const string ClientName = "MerchantBillingDelegation";

    internal static void AddMerchantBillingDelegation(this IServiceCollection services)
    {
        services.AddOptions<MerchantBillingDelegationOptions>().BindConfiguration(MerchantBillingDelegationOptions.Section);
        services.AddSingleton<IMerchantBillingSigningKeys, MerchantBillingDelegationKeys>();
        services.AddScoped<MerchantBillingDelegationIssuer>();
        services.AddHttpClient<IMerchantBillingAccess, MerchantBillingClient>(ClientName)
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
