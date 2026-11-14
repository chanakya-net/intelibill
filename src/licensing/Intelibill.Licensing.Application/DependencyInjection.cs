using Intelibill.Licensing.Application.Bootstrap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Intelibill.Licensing.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddLicensingApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<PrepareBootstrapAdminHandler>();
        services.AddScoped<BootstrapAdminHandler>();
        return services;
    }
}
