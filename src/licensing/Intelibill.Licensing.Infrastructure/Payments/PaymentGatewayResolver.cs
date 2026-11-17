using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Infrastructure.Payments.Development;
using Intelibill.Licensing.Infrastructure.Payments.Razorpay;
using Intelibill.Shared.Contracts.V1;
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
/// selected initially by <c>Payments:Mode = LocalSimulator</c> in a local host. Historical resolution is independent
/// of the selected provider; a simulator is always refused outside Development/Testing.
/// </summary>
internal sealed class PaymentGatewayResolver(IServiceProvider services, IOptions<PaymentOptions> options,
    IHostEnvironment environment, IOptions<RazorpayOptions> razorpay, PaymentProviderConfiguration registry, PaymentProviderBindings bindings) : IPaymentGatewayResolver, IPaymentProviderRegistry
{
    internal static bool SimulatorEnabled(PaymentOptions options, IHostEnvironment environment) =>
        options.Mode == PaymentOptions.LocalSimulator && (environment.IsDevelopment() || environment.IsEnvironment("Testing"));

    public IPaymentGateway? ForNewAttempts() => options.Value.Mode == PaymentOptions.RazorpayMode ? Razorpay()
        : SimulatorEnabled(options.Value, environment) ? Simulator() : null;

    /// <summary>An attempt is served only by the adapter whose exact account, environment and version it was pinned to.</summary>
    public IPaymentGateway? ForPin(PaymentProviderPin pin)
    {
        var matches = registry.Profiles.Where(profile => profile.Pin == pin).ToArray();
        if (matches.Length != 0)
            return matches is [var profile] && Failure(profile) is null ? Create(profile) : null;
        var legacy = pin == DevelopmentPaymentGateway.LocalPin ? (IPaymentGateway?)Simulator()
            : pin.ProviderId == RazorpayConfiguration.ProviderId && Razorpay() is { } gateway && gateway.Pin == pin ? gateway : null;
        return legacy is not null && !bindings.IsRegistered(pin) ? legacy : null;
    }

    public IReadOnlyList<IPaymentGateway> ForWebhooks(string providerId)
    {
        var configured = registry.Profiles.Where(p => p.ProviderId == providerId && Failure(p) is null)
            .Select(Create).OfType<IPaymentGateway>().ToList();
        var legacy = providerId == DevelopmentPaymentGateway.ProviderId ? (IPaymentGateway?)Simulator() : Razorpay();
        if (legacy?.Pin.ProviderId == providerId && configured.All(g => g.Pin != legacy.Pin) && ForPin(legacy.Pin) is { } retained)
            configured.Add(retained);
        return configured.Where(g => g.Capabilities.Webhooks).ToArray();
    }

    public IReadOnlyList<PaymentProviderProfileStatus> Profiles()
    {
        var profiles = registry.Profiles.Select(p => new PaymentProviderProfileStatus(p.Id, p.Pin,
            Failure(p) is null, Failure(p))).ToList();
        if (Simulator() is { } simulator)
            profiles.Add(new("legacy-simulator", simulator.Pin, ForPin(simulator.Pin) is not null,
                ForPin(simulator.Pin) is null ? "InvalidProfile" : null));
        if (Razorpay() is { } razorpayGateway)
            profiles.Add(new("legacy-razorpay", razorpayGateway.Pin, ForPin(razorpayGateway.Pin) is not null,
                ForPin(razorpayGateway.Pin) is null ? "InvalidProfile" : null));
        return profiles;
    }

    private string? Failure(PaymentProviderProfile profile)
    {
        if (registry.Failure(profile) is { } invalid) return invalid;
        if (profile.ProviderId == RazorpayConfiguration.ProviderId &&
            RazorpayConfiguration.From(razorpay.Value) is { } legacy && legacy.Pin == profile.Pin &&
            registry.Razorpay(profile) is { } retained && !legacy.SameConfiguration(retained))
            return "InvalidProfile";
        return bindings.Matches(profile) ? null : "ConfigurationReused";
    }

    private IPaymentGateway? Create(PaymentProviderProfile profile)
    {
        if (profile.ProviderId == DevelopmentPaymentGateway.ProviderId)
            return new DevelopmentPaymentGateway(services.GetRequiredService<Data.LicensingDbContext>(),
                services.GetRequiredKeyedService<TimeProvider>(CheckoutOrdersV1.Policy), registry.Webhook(profile), profile.Pin);
        return registry.Razorpay(profile) is { } config && config.Pin == profile.Pin ? CreateRazorpay(config) : null;
    }

    private RazorpayGateway? Razorpay()
    {
        if (RazorpayConfiguration.From(razorpay.Value) is not { } config)
            return null;
        return CreateRazorpay(config);
    }

    private RazorpayGateway CreateRazorpay(RazorpayConfiguration config)
    {
        var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(RazorpayGateway.ClientName);
        client.BaseAddress = config.BaseAddress;
        return new(client, config, services.GetRequiredKeyedService<TimeProvider>(CheckoutOrdersV1.Policy));
    }

    private DevelopmentPaymentGateway? Simulator() => (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        ? services.GetRequiredService<DevelopmentPaymentGateway>() : null;
}
