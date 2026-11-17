using Intelibill.Licensing.Application.Payments;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Integration.Tests;

[Collection(MerchantBillingHosts.Name)]
public sealed partial class P0310Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    internal static readonly PaymentProviderPin A = new("development-simulator", "simulator-a", "Development", "a/1");
    internal static readonly PaymentProviderPin B = new("development-simulator", "simulator-b", "Development", "b/1");

    internal static Dictionary<string, string?> Settings() => new(P0302Fixture.SimulatorSettings)
    {
        ["Payments:Profiles:0:Id"] = "a",
        ["Payments:Profiles:0:ProviderId"] = A.ProviderId,
        ["Payments:Profiles:0:AccountId"] = A.ProviderAccountId,
        ["Payments:Profiles:0:Environment"] = A.Environment,
        ["Payments:Profiles:0:ConfigurationVersion"] = A.ConfigurationVersion,
        ["Payments:Profiles:1:Id"] = "b",
        ["Payments:Profiles:1:ProviderId"] = B.ProviderId,
        ["Payments:Profiles:1:AccountId"] = B.ProviderAccountId,
        ["Payments:Profiles:1:Environment"] = B.Environment,
        ["Payments:Profiles:1:ConfigurationVersion"] = B.ConfigurationVersion,
    };

    [Fact]
    public async Task RetainedProfilesResolveExactPinsAndIsolateSimulatorState()
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = P0302Fixture.WholeSecondStart }, Settings());
        using var scope = fixture.Hosts.Licensing.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IPaymentGatewayResolver>();
        var first = resolver.ForPin(A);
        var second = resolver.ForPin(B);
        Assert.NotNull(first);
        Assert.NotNull(second);
        var request = new CheckoutRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 12000, "INR");
        var created = await first.CreateCheckoutAsync(request, default);
        Assert.Equal(GatewayOutcome.Succeeded, created.Outcome);
        var reference = created.Value!.ProviderOrderReference;
        Assert.Equal(GatewayOutcome.NotFound, (await second.LookupCheckoutAsync(request, default)).Outcome);
        await ((IPaymentSimulator)second).SimulateAsync(reference, "Captured", default);
        Assert.Empty((await first.GetOrderPaymentsAsync(reference, default)).Value!);
        await ((IPaymentSimulator)first).SimulateAsync(reference, "Captured", default);
        var payment = Assert.Single((await first.GetOrderPaymentsAsync(reference, default)).Value!);
        Assert.Equal(A, payment.Pin);
        Assert.Empty((await second.GetOrderPaymentsAsync(reference, default)).Value!);
        Assert.Equal(GatewayOutcome.Succeeded, (await first.GetPaymentAsync(payment.ProviderPaymentReference!, default)).Outcome);
        Assert.NotEqual(GatewayOutcome.Succeeded, (await second.GetPaymentAsync(payment.ProviderPaymentReference!, default)).Outcome);
        Assert.Null(resolver.ForPin(A with { ConfigurationVersion = "a/missing" }));
    }
}
