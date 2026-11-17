using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Infrastructure.Payments.Development;
using Intelibill.Licensing.Infrastructure.Payments.Razorpay;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Intelibill.Licensing.Infrastructure.Payments;

internal sealed class PaymentOptions
{
    public const string Section = "Payments";
    public const string LocalSimulator = "LocalSimulator";
    public const string RazorpayMode = "Razorpay";
    public string? Mode { get; init; }
    public bool ReconcileInBackground { get; init; } = true;
}

/// <summary>
/// Chooses the active adapter for new attempts and resolves the pinned adapter for older ones. The simulator is
/// honoured only when <c>Payments:Mode = LocalSimulator</c> in a Development or Testing host; otherwise no adapter
/// exists and online payment is unavailable.
/// </summary>
internal sealed class PaymentGatewayResolver(IServiceProvider services, IOptions<PaymentOptions> options,
    IHostEnvironment environment, IOptions<RazorpayOptions> razorpay) : IPaymentGatewayResolver
{
    internal static bool SimulatorEnabled(PaymentOptions options, IHostEnvironment environment) =>
        options.Mode == PaymentOptions.LocalSimulator && (environment.IsDevelopment() || environment.IsEnvironment("Testing"));

    public IPaymentGateway? ForNewAttempts() => options.Value.Mode == PaymentOptions.RazorpayMode ? Razorpay() : Simulator();

    /// <summary>An attempt is served only by the adapter whose exact account, environment and version it was pinned to.</summary>
    public IPaymentGateway? ForPin(PaymentProviderPin pin) =>
        pin == DevelopmentPaymentGateway.LocalPin ? Simulator()
            : pin.ProviderId == RazorpayConfiguration.ProviderId && Razorpay() is { } gateway && gateway.Pin == pin ? gateway : null;

    private RazorpayGateway? Razorpay()
    {
        if (RazorpayConfiguration.From(razorpay.Value) is not { } config)
            return null;
        var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(RazorpayGateway.ClientName);
        client.BaseAddress = config.BaseAddress;
        return new(client, config);
    }

    private DevelopmentPaymentGateway? Simulator() => SimulatorEnabled(options.Value, environment)
        ? services.GetRequiredService<DevelopmentPaymentGateway>() : null;
}
