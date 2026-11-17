using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// R004: the order's own attempt counter must be matched by its payment collection before an order counts as
/// provably unpaid. A collection missing an attempted payment cannot rule out a debit already in flight.
/// </summary>
public sealed partial class P0304Tests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    public async Task AttemptedOrderWithAnIncompletePaymentCollectionStaysPendingUntilTheMissingPaymentSettles(
        int attempts, int oldFailures)
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        for (var failure = 0; failure < oldFailures; failure++)
            rig.Fake.AddPayment(order, "failed");
        var missing = rig.Fake.AddPayment(order, "created");
        rig.Fake.HideFromCollection(missing);
        Assert.Equal(attempts, rig.Fake.Order(order)["attempts"]!.GetValue<int>());
        Assert.Equal("attempted", rig.Fake.Order(order)["status"]!.GetValue<string>());

        // Every listed failure is past the late-authorization window; only the missing attempt keeps funds unknown.
        rig.Fixture.CheckoutClock.Now = rig.Fixture.CheckoutClock.Now.AddDays(4).AddSeconds(1);
        await AssertHeldWithoutLauncherAsync(rig, await rig.VerifyAsync(attempt, null, null));
        await AssertHeldWithoutLauncherAsync(rig, await rig.VerifyAsync(attempt, null, null));

        rig.Fake.RevealInCollection(missing);
        rig.Fake.Transition(missing, "captured");
        AssertStatus(await rig.VerifyAsync(attempt, null, null), "Verified", "Fulfilled", "Applying");
        AssertStatus(await StatusAsync(rig.Payer.Client, rig.Quote), "Verified", "Fulfilled", "Applying");
        Assert.Equal(1, rig.Fake.Posts);
        await P0302Tests.AssertSingleFulfilmentAsync(rig.Fixture, rig.Quote, 1);
    }

    /// <summary>A collection whose stated count disagrees with its items is incomplete evidence, never unpaid.</summary>
    [Fact]
    public async Task PaymentCollectionWhoseCountDisagreesWithItsItemsNeverResumesTheOrder()
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        rig.Fake.AddPayment(order, "failed");
        rig.Fake.CollectionCountOffset = 1;

        rig.Fixture.CheckoutClock.Now = rig.Fixture.CheckoutClock.Now.AddDays(4).AddSeconds(1);
        await AssertHeldWithoutLauncherAsync(rig, await rig.VerifyAsync(attempt, null, null));

        rig.Fake.CollectionCountOffset = 0;
        var resumed = await rig.VerifyAsync(attempt, null, null);
        AssertStatus(resumed, "AwaitingPayment", "NotFulfilled", "NotApplicable");
        Assert.Equal(order, resumed.GetProperty("launch").GetProperty("parameters").GetProperty("orderId").GetString());
        Assert.Equal(1, rig.Fake.Posts);
    }
}
