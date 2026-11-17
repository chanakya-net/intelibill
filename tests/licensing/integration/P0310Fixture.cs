using Intelibill.Licensing.Application.Payments;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>Represents the pre-routing deployment selector only when constructing legacy migration fixtures.</summary>
internal sealed class P0310LegacyRouting
{
    internal bool BeforeRouting { get; set; } = true;
    internal void Install(IServiceCollection services) => services.AddScoped<IPaymentProviderSelection>(provider =>
        new LegacySelection(this, provider.GetRequiredService<IPaymentProviderRoutingStore>(), provider.GetRequiredService<IPaymentGatewayResolver>()));
    internal static void InstallLegacy(IServiceCollection services) => new P0310LegacyRouting().Install(services);

    private sealed class LegacySelection(P0310LegacyRouting fixture, IPaymentProviderSelection current,
        IPaymentGatewayResolver gateways) : IPaymentProviderSelection
    {
        public Task<PaymentProviderSelection> SelectedAsync(bool intention, CancellationToken ct) => fixture.BeforeRouting
            ? Task.FromResult(new PaymentProviderSelection(0, null, gateways.ForNewAttempts()?.Pin))
            : current.SelectedAsync(intention, ct);
    }
}
