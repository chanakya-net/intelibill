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

    [Fact]
    public async Task ReductionAppliesAtRenewalAfterPaidCapacityIncreaseWhenIncreaseSnapshotLags()
    {
        await using var fixture = await StartAsync(database, January31);
        var package = await fixture.PackageAsync(2, ShopNames);
        var (_, term) = await PurchaseAsync(package, "Month");
        var delivery = fixture.Checkout.Delivery;
        await delivery.ScanAsync(true);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);

        // Apply a #910 paid capacity increase (3 -> 4 slots) and drain Reserve/Consume receipts,
        // leaving the post-apply #910 Snapshot for Shops[1] delayed until after renewal.
        var successorPlan = await RepriceAsync(fixture, package, 18000L);
        fixture.At(February14);
        using var incQuoted = await PostQuoteAsync(package, term, Request(successorPlan, 4, 3, February29));
        incQuoted.EnsureSuccessStatusCode();
        var incQuote = await incQuoted.Content.ReadFromJsonAsync<JsonElement>();
        var incPath = $"{QuotesPath(package.Account, term)}/{incQuote.GetProperty("quoteId").GetGuid():D}/order";
        using var incConverted = await package.Payer.Client.PostAsJsonAsync(incPath,
            new { operationKey = Guid.NewGuid(), snapshotHash = incQuote.GetProperty("snapshotHash").GetString() });
        incConverted.EnsureSuccessStatusCode();
        var incOrder = await AcceptAsync(package, (await incConverted.Content.ReadFromJsonAsync<CheckoutQuoteV1>())!);
        await PayAsync(package, incOrder);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);
        Assert.Equal(4, (await AccountTermAsync(package, term)).GetProperty("purchasedCapacity").GetInt32());

        // Hold back Shops[1]'s #910 snapshot so merchant shop_license_states still shows shop_capacity = 3.
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            UPDATE public.entitlement_outbox o
            SET next_attempt_at = '{February29.AddDays(2):O}'::timestamptz
            FROM public.entitlement_messages m
            WHERE m.event_id = o.event_id
                AND o.account_id = '{package.Account:D}' AND o.shop_id = '{package.Shops[1].ShopId:D}'
                AND m.kind = 'Snapshot' AND o.status = 'Pending'
            """);
        Assert.Equal(3, await delivery.MerchantScalarAsync(
            $"SELECT shop_capacity FROM public.shop_license_states WHERE shop_id = '{package.Shops[1].ShopId:D}'"));

        // Preview, schedule, fund and apply a #911 reduction (4 -> 1) over the increased term.
        var solo = await PublishReductionPlanAsync(fixture, "single-shop", "Solo After Increase", 1, 3, 6000L);
        var preview = await CreatePreviewAsync(package, term, solo, 1, 4, 5, February29);
        Assert.Equal(4, preview.PriorCapacity);
        Assert.Equal(3, preview.SlotsRemoved);
        var schedule = await ScheduleAsync(package, term, preview, [package.Shops[0].ShopId]);
        var order = await AcceptAsync(package, await CreateReductionOrderAsync(package, term, schedule));
        await PayAsync(package, order);

        fixture.At(February29);
        await DrainReductionAsync(delivery, rounds: 4);

        var applied = await ReadScheduleAsync(package, term);
        Assert.Equal("Applied", applied.State);
        Assert.NotNull(applied.AppliedTermId);
        Assert.Equal(2L, await fixture.CountAsync("subscription_coverage_terms"));
        Assert.Equal("Active", await delivery.MerchantScalarAsync(
            $"SELECT state FROM public.shop_license_states WHERE shop_id = '{package.Shops[0].ShopId:D}'"));
        Assert.Equal(applied.AppliedTermId!.Value, await delivery.MerchantScalarAsync(
            $"SELECT term_id FROM public.shop_license_states WHERE shop_id = '{package.Shops[0].ShopId:D}'"));
        Assert.Equal("Expired", await delivery.MerchantScalarAsync(
            $"SELECT state FROM public.shop_license_states WHERE shop_id = '{package.Shops[1].ShopId:D}'"));
    }
}
