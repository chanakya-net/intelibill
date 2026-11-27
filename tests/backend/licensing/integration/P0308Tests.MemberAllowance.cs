using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0308Tests
{
    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    public async Task EarlyRenewalUsesEachPurchasedMemberAllowanceAtItsOwnBoundary(int successorAllowance)
    {
        await using var fixture = await StartAsync(database, March31);
        var package = await fixture.PackageAsync(2);
        var (_, prior) = await PurchaseAsync(package, "Month");
        var plan = (Guid)(await fixture.ScalarAsync($"SELECT plan_id FROM plan_versions WHERE id = '{package.Plan:D}'"))!;
        var successor = await P0301Tests.PublishPlanAsync(fixture.Checkout.Trial, plan, package.Plan, successorAllowance);
        var delivery = fixture.Checkout.Delivery;

        fixture.At(April30.AddHours(-2));
        await delivery.ScanAsync(source: true);
        await AssertDeliveredAllowanceAsync(delivery, package, 5);
        var (quote, paid) = await RenewAsync(package with { Plan = successor }, prior, "Month");
        P0302Fixture.AssertStatus(paid, "Verified", "Fulfilled", "Applying");
        await AssertPaidRulesAsync(package, expectedAllowance: 5, April30);
        var published = await delivery.LatestSnapshotAsync(package.Payer.ShopId);
        Assert.Equal("Active", published.Shop!.State);
        Assert.Equal(5, published.Shop.MemberAllowance);
        Assert.Equal(May31.AddDays(7), published.Shop.AccessEndsAt);
        Assert.Equal(April30, published.Shop.NextBoundary);
        Assert.Equal(April30.UtcDateTime, Assert.IsType<DateTime>(await fixture.ScalarAsync($"""
            SELECT next_verification_at FROM entitlement_shop_heads WHERE shop_id = '{package.Payer.ShopId:D}'
            """)));
        await AssertDeliveredAllowanceAsync(delivery, package, 5);
        P0302Fixture.AssertStatus(await P0302Fixture.StatusAsync(package.Payer.Client, quote), "Verified", "Fulfilled", "Applied");

        fixture.At(April30.AddMicroseconds(-1));
        await AssertPaidRulesAsync(package, expectedAllowance: 5, April30);
        await delivery.ScanAsync(source: true);
        Assert.Equal(published.EventId, (await delivery.LatestSnapshotAsync(package.Payer.ShopId)).EventId);

        fixture.At(April30);
        await AssertPaidRulesAsync(package, successorAllowance, May31);
        await delivery.ScanAsync(source: true);
        var refreshed = await delivery.LatestSnapshotAsync(package.Payer.ShopId);
        Assert.NotEqual(published.EventId, refreshed.EventId);
        Assert.Equal("Active", refreshed.Shop!.State);
        Assert.Equal(successorAllowance, refreshed.Shop.MemberAllowance);
        Assert.Equal(May31.AddDays(7), refreshed.Shop.AccessEndsAt);
        Assert.Equal(May31, refreshed.Shop.NextBoundary);
        await AssertDeliveredAllowanceAsync(delivery, package, successorAllowance);
    }

    private static async Task AssertPaidRulesAsync(Package package, int expectedAllowance, DateTimeOffset next)
    {
        foreach (var shop in package.Shops)
        {
            var status = await ShopTermAsync(package, shop);
            Assert.Equal("Active", status.GetProperty("state").GetString());
            Assert.Equal(3, status.GetProperty("purchasedCapacity").GetInt32());
            Assert.Equal(expectedAllowance, status.GetProperty("memberAllowance").GetInt32());
            Assert.Equal(May31, status.GetProperty("paidThrough").GetDateTimeOffset());
            Assert.Equal(next, status.GetProperty("nextDeadline").GetDateTimeOffset());
        }
    }

    private static async Task AssertDeliveredAllowanceAsync(P0213Fixture delivery, Package package, int expected)
    {
        await delivery.DrainAsync(source: true);
        await delivery.DrainAsync(source: false);
        foreach (var shop in package.Shops)
            Assert.Equal(expected, await delivery.MerchantScalarAsync($"""
                SELECT member_allowance FROM shop_license_states WHERE shop_id = '{shop.ShopId:D}'
                """));
    }
}
