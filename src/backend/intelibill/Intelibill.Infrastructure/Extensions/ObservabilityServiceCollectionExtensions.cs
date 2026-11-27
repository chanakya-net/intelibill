using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Infrastructure.Extensions;

public static class ObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddObservabilityOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ObservabilityOptions>()
            .BindConfiguration(ObservabilityOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<CircuitBreakerOptions>()
            .BindConfiguration(CircuitBreakerOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
