using Intelibill.Infrastructure.Billing;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Intelibill.Api.Unit.Tests.Infrastructure.Billing;

/// <summary>
/// The merchant relay is the browser's only source of launcher instructions. A simulator launch from upstream is
/// relayed only by a Development or Testing merchant host; elsewhere the payment state survives without a launcher.
/// </summary>
public sealed class MerchantCheckoutPaymentRelayTests
{
    private static readonly Guid Account = Guid.NewGuid();
    private static readonly Guid Order = Guid.NewGuid();

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void SimulatorLaunchNeverReachesTheBrowserOutsideLocalHosts(string environment)
    {
        var relayed = MerchantBillingClient.RelayPayment(SimulatorShaped(), Account, Order, Host(environment));

        Assert.NotNull(relayed);
        Assert.Null(relayed.Launch);
        Assert.Equal("AwaitingPayment", relayed.Payment);
        Assert.Equal(SimulatorShaped().AttemptId, relayed.AttemptId);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void LocalHostsRelayTheSimulatorLaunchWithoutParameters(string environment)
    {
        var relayed = MerchantBillingClient.RelayPayment(SimulatorShaped(), Account, Order, Host(environment));

        Assert.Equal(CheckoutPaymentsV1.SimulatorFlow, relayed?.Launch?.Flow);
        Assert.Empty(relayed!.Launch!.Parameters);
    }

    [Fact]
    public void InvalidUpstreamPaymentIsRefusedInEveryHost()
    {
        var foreign = SimulatorShaped() with { AccountId = Guid.NewGuid() };

        Assert.Null(MerchantBillingClient.RelayPayment(foreign, Account, Order, Host("Development")));
        Assert.Null(MerchantBillingClient.RelayPayment(foreign, Account, Order, Host("Production")));
    }

    private static CheckoutPaymentV1 SimulatorShaped() => new(Account, Order, Guid.Parse("8c2b3c5e-0d5e-4bb4-9a43-6f0f1f0e2a11"),
        "AwaitingPayment", "NotFulfilled", "NotApplicable", 99_900, "INR", DateTimeOffset.UnixEpoch, null, null, null,
        null, 1, 0, false, new(CheckoutPaymentsV1.SimulatorFlow, new Dictionary<string, string>()));

    private static IHostEnvironment Host(string name)
    {
        var host = Substitute.For<IHostEnvironment>();
        host.EnvironmentName.Returns(name);
        return host;
    }
}
