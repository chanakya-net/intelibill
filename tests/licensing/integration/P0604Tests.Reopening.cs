using System.Net;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0604Tests
{
    [Fact]
    public async Task ReviewedReopeningOfAnAssignedShopRequiresAMatchingMerchantReceipt()
    {
        await using var fixture = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(fixture);
        var shop = p.Shops[2];
        var delivery = fixture.Checkout.Delivery;
        var assignment = await AcceptSlotAsync(p, term, shop);
        await delivery.DrainAsync(false);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);
        await AssertViewsAsync(p, term, shop, assignment, "Acknowledged", "Applied", "Acknowledged");

        var route = await P0417Tests.CloseShopAsync(shop, p.Account);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);
        await AssertViewsAsync(p, term, shop, assignment, "Closed", "Applied", "Closed");

        using var manager = await P0417Tests.SignInAsync(delivery);
        var command = P0417Tests.Command(await P0417Tests.PreviewAsync(manager, route), shop);
        using var reopened = await P0212Fixture.PostAsync(manager, route, command);
        Assert.True(reopened.StatusCode == HttpStatusCode.Created, await reopened.Content.ReadAsStringAsync());
        await AssertViewsAsync(p, term, shop, assignment, "Applying", "Applied", "Closed");

        var opening = await LatestSnapshotAsync(fixture, shop.ShopId);
        await P0403Tests.QueueReconciliationAsync(delivery, shop.ShopId);
        var successor = await LatestSnapshotAsync(fixture, shop.ShopId);
        Assert.NotEqual(opening, successor);
        await DeliverSingleSnapshotAsync(fixture, successor, expectOutcome: "Applied");
        await AssertViewsAsync(p, term, shop, assignment, "Applying", "Applied", "AppliedLocally");
        await delivery.DrainAsync(false);
        await AssertViewsAsync(p, term, shop, assignment, "Acknowledged", "Applied", "Acknowledged");

        await DeliverSingleSnapshotAsync(fixture, successor, expectOutcome: "Applied");
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);
        await AssertViewsAsync(p, term, shop, assignment, "Acknowledged", "Applied", "Acknowledged");
        Assert.Equal(1L, await fixture.CountAsync("operational_closure_reopenings"));
    }
}
