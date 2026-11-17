using Intelibill.Licensing.Application.Entitlements;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>Migration fixtures for releases that predate entitlement delivery seed their original facts only.</summary>
internal sealed class PreEntitlementPublisher : IEntitlementPublisher
{
    public Task PublishAsync(Guid account, Guid shop, CancellationToken ct, DateTimeOffset? evaluatedAt = null) => Task.CompletedTask;
}

internal static class PreEntitlementFixtureRegistration
{
    internal static void AddPreEntitlementPublisher(this IServiceCollection services) =>
        services.AddScoped<IEntitlementPublisher, PreEntitlementPublisher>();
}
