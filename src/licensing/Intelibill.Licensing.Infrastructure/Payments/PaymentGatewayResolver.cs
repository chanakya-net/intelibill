using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Infrastructure.Payments.Development;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Intelibill.Licensing.Infrastructure.Payments;

internal sealed class PaymentOptions
{
    public const string Section = "Payments";
    public const string LocalSimulator = "LocalSimulator";
    public string? Mode { get; init; }
}

/// <summary>
/// Chooses the active adapter for new attempts and resolves the pinned adapter for older ones. The simulator is
/// honoured only when <c>Payments:Mode = LocalSimulator</c> in a Development or Testing host; otherwise no adapter
/// exists and online payment is unavailable.
/// </summary>
internal sealed class PaymentGatewayResolver(IServiceProvider services, IOptions<PaymentOptions> options,
    IHostEnvironment environment) : IPaymentGatewayResolver
{
    internal static bool SimulatorEnabled(PaymentOptions options, IHostEnvironment environment) =>
        options.Mode == PaymentOptions.LocalSimulator && (environment.IsDevelopment() || environment.IsEnvironment("Testing"));

    public IPaymentGateway? ForNewAttempts() => Simulator();

    public IPaymentGateway? ForPin(PaymentProviderPin pin) =>
        pin == DevelopmentPaymentGateway.LocalPin ? Simulator() : null;

    private DevelopmentPaymentGateway? Simulator() => SimulatorEnabled(options.Value, environment)
        ? services.GetRequiredService<DevelopmentPaymentGateway>() : null;
}
