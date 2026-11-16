using Intelibill.Licensing.Application.Grants;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Infrastructure.Status;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Infrastructure.Grants;

internal static class ComplimentaryGrantRegistration
{
    /// <summary>
    /// Grants judge their dates against the same lifecycle clock status evaluations read (the application clock
    /// unless a test host fixes it); sessions and recent MFA use the application clock.
    /// </summary>
    internal static void AddComplimentaryGrants(this IServiceCollection services)
    {
        services.AddScoped<IComplimentaryGrantStore, ComplimentaryGrantStore>();
        services.AddScoped(provider => new GrantComplimentaryAccessHandler(
            provider.GetRequiredService<IComplimentaryGrantStore>(), provider.GetRequiredService<ISubscriptionAccessFactsReader>(),
            provider.GetRequiredKeyedService<TimeProvider>(SubscriptionStatusRegistration.ClockKey),
            provider.GetRequiredService<TimeProvider>()));
    }
}
