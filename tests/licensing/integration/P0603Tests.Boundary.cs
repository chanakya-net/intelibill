using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0603Tests
{
    [Theory]
    [InlineData("single-shop", 1, "Independent")]
    [InlineData("multi-shop", 2, "Grouped")]
    public async Task OnlySelectedShopsReceiveOnePaidSuccessorAtTheBoundary(string targetKind, int targetCapacity, string expectedMode)
    {
        await using var fixture = await StartAsync(database, January31);
        var package = await fixture.PackageAsync(3, ThreeShops);
        var (_, term) = await PurchaseAsync(package, "Month");
        var delivery = fixture.Checkout.Delivery;
        await delivery.ScanAsync(true);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);

        var targetPlan = await PublishReductionPlanAsync(fixture, targetKind, $"Target {expectedMode}",
            targetCapacity, membersPerShop: 3, monthlyPricePaise: targetCapacity == 1 ? 6000L : 9000L);
        fixture.At(February14);

        var preview = await CreatePreviewAsync(package, term, targetPlan, targetCapacity, 3, 5, February29);
        Assert.Equal(expectedMode, preview.TargetMode);
        var retained = package.Shops.Take(targetCapacity).Select(s => s.ShopId).ToArray();
        var omitted = package.Shops.Skip(targetCapacity).Select(s => s.ShopId).ToArray();
        var schedule = await ScheduleAsync(package, term, preview, retained);
        var order = await AcceptAsync(package, await CreateReductionOrderAsync(package, term, schedule));
        await PayAsync(package, order);

        // Before paidThrough: still Funded, predecessor still active for all 3 shops.
        fixture.At(February29.AddTicks(-10));
        await DrainReductionAsync(delivery);
        Assert.Equal("Funded", (await ReadScheduleAsync(package, term)).State);
        Assert.Equal(1L, await fixture.CountAsync("subscription_coverage_terms"));

        // At paidThrough: durable scan queues Manifest -> Reserve -> Consume -> Applied successor!
        fixture.At(February29);
        await DrainReductionAsync(delivery, rounds: 4);

        var applied = await ReadScheduleAsync(package, term);
        Assert.Equal("Applied", applied.State);
        Assert.NotNull(applied.AppliedTermId);
        var successorId = applied.AppliedTermId!.Value;
        Assert.Equal(2L, await fixture.CountAsync("subscription_coverage_terms"));
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments",
            $"order_id = '{order.OrderId:D}' AND predecessor_term_id = '{term:D}' AND term_id = '{successorId:D}'"));

        // Check exact retained shop links and successor mode/anchor/dates.
        var linked = await fixture.ScalarAsync($"""
            SELECT array_agg(shop_id ORDER BY shop_id) FROM payment_fulfilment_shops WHERE term_id = '{successorId:D}'
            """) as Guid[];
        Assert.Equal(retained.Order().ToArray(), linked);

        var status = await AccountTermAsync(package, successorId);
        Assert.Equal("Active", status.GetProperty("state").GetString());
        Assert.Equal(expectedMode, status.GetProperty("coverageMode").GetString());
        Assert.Equal(targetCapacity, status.GetProperty("purchasedCapacity").GetInt32());
        Assert.Equal(3, status.GetProperty("memberAllowance").GetInt32());
        Assert.Equal(February29, status.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal(P0209Fixture.Utc("2028-03-31T04:30:00Z"), status.GetProperty("paidThrough").GetDateTimeOffset());

        var paidStatus = await P0302Fixture.StatusAsync(package.Payer.Client, order);
        P0302Fixture.AssertStatus(paidStatus, "Verified", "Fulfilled", "Applied");

        // Entitlement snapshots reached BOTH retained (new Active successor) and omitted (Expired with no grace) shops.
        foreach (var shopId in retained)
        {
            Assert.Equal("Active", await delivery.MerchantScalarAsync(
                $"SELECT state FROM shop_license_states WHERE shop_id = '{shopId:D}'"));
            Assert.Equal(successorId, await delivery.MerchantScalarAsync(
                $"SELECT term_id FROM shop_license_states WHERE shop_id = '{shopId:D}'"));
        }
        foreach (var shopId in omitted)
        {
            Assert.Equal("Expired", await delivery.MerchantScalarAsync(
                $"SELECT state FROM shop_license_states WHERE shop_id = '{shopId:D}'"));
        }

        // Ordinary renewal of the reduced successor works seamlessly on the new capacity and anchor.
        fixture.At(P0209Fixture.Utc("2028-03-15T04:30:00Z"));
        using var nextRes = await package.Payer.Client.PostAsJsonAsync($"/api/billing/accounts/{package.Account:D}/orders",
            new { operationKey = Guid.NewGuid(), planVersionId = targetPlan, period = "Month", shopIds = Array.Empty<Guid>(),
                extraShops = 0, expectedRevision = 1, termId = successorId });
        nextRes.EnsureSuccessStatusCode();
        var nextRenewal = await AcceptAsync(package, (await nextRes.Content.ReadFromJsonAsync<CheckoutQuoteV1>())!);
        Assert.Equal(targetCapacity, nextRenewal.Capacity);
        Assert.Equal(targetCapacity, nextRenewal.ShopCount);
        await PayAsync(package, nextRenewal);
        Assert.Equal(3L, await fixture.CountAsync("subscription_coverage_terms"));
    }
}
