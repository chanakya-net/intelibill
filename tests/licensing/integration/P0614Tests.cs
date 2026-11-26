using System.Net;
using System.Net.Http.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// P06-14: composed commercial-management acceptance. Each fact joins two merged P06 slices on one shared shop, term or
/// receipt identity through the production HTTP, signed delivery and worker paths. Real PostgreSQL for both stores,
/// fixed business clocks and the local payment simulator only; never claims live provider behavior.
/// </summary>
public sealed partial class P0614Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    /// <summary>
    /// Stories 2–4: a spare paid slot consumed after the owner reviewed a reduction invalidates that selection, and the
    /// renewal applies only the shops the owner explicitly retains from a fresh preview, without touching any roster.
    /// </summary>
    [Fact]
    public async Task FinalSlotReceiptInvalidatesEarlierReductionSelection()
    {
        await using var fixture = await StartAsync(database, January31);
        var (p, term) = await P0604Tests.PrepareAsync(fixture);
        var delivery = fixture.Checkout.Delivery;
        var (central, east, north) = (p.Shops[0], p.Shops[1], p.Shops[2]);
        await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Staff, central.ShopId);
        var roster = await RosterAsync(fixture, p);
        var dual = await P0603Tests.PublishReductionPlanAsync(fixture, "multi-shop", "P0614 Dual", 2, 3, 9000L);
        var stale = await P0603Tests.CreatePreviewAsync(p, term, dual, 2, 3, 5, February29);
        Assert.Equal(2, stale.SourceShopCount);
        var orders = await fixture.CountAsync("checkout_orders");

        var assignment = await P0604Tests.AcceptSlotAsync(p, term, north);
        var receipt = await ConsentReceiptAsync(fixture, north.ShopId);
        await DrainSlotAsync(delivery);
        await AssertSlotAppliedOnceAsync(fixture, p, term, assignment, north);

        using var refused = await P0603Tests.PostScheduleAsync(p, term, stale, [central.ShopId, east.ShopId]);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(CapacityReductionsV1.SourceChanged, await P0206Fixture.CodeAsync(refused));
        Assert.Equal(0L, await fixture.CountAsync("scheduled_capacity_reductions"));
        Assert.Equal(orders, await fixture.CountAsync("checkout_orders"));

        using var replayed = await DeliverConsentAsync(delivery, receipt);
        Assert.Equal(HttpStatusCode.NoContent, replayed.StatusCode);
        await DrainSlotAsync(delivery);
        await AssertSlotAppliedOnceAsync(fixture, p, term, assignment, north);

        var schedule = await ScheduleFreshSelectionAsync(p, term, dual, north.ShopId, [east.ShopId, north.ShopId]);
        var order = await AcceptAsync(p, await P0603Tests.CreateReductionOrderAsync(p, term, schedule));
        await PayAsync(p, order);
        await AssertRenewalHeldForReviewAsync(fixture, p, term, order.OrderId, north.ShopId);
        Assert.Equal(roster, await RosterAsync(fixture, p));
        Assert.Equal(0L, await delivery.MerchantScalarAsync($"""
            SELECT count(*) FROM shop_memberships WHERE user_id='{p.Payer.UserId:D}' AND shop_id<>'{central.ShopId:D}'
            """));
    }

    private static async Task DrainSlotAsync(P0213Fixture delivery)
    {
        await delivery.DrainAsync(false);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);
    }

    private static async Task AssertSlotAppliedOnceAsync(P0308Fixture fixture, Package p, Guid term, Guid assignment,
        P0209Fixture.Merchant shop)
    {
        Assert.Equal("Applied", await fixture.ScalarAsync(
            $"SELECT state FROM coverage_slot_assignments WHERE assignment_id='{assignment:D}'"));
        Assert.Equal(1L, await fixture.CountAsync("coverage_slot_assignments"));
        Assert.Equal(3L, await fixture.CountAsync("payment_fulfilment_shops", $"term_id='{term:D}'"));
        Assert.Equal(1L, await fixture.CountAsync("subscription_coverage_terms"));
        Assert.Equal(February29, (await ShopTermAsync(p, shop)).GetProperty("paidThrough").GetDateTimeOffset());
    }

    private static async Task<ScheduledCapacityReductionV1> ScheduleFreshSelectionAsync(Package p, Guid term, Guid plan,
        Guid addedShop, Guid[] retained)
    {
        var fresh = await P0603Tests.CreatePreviewAsync(p, term, plan, 2, 3, 5, February29);
        Assert.Equal(3, fresh.SourceShopCount);
        Assert.Equal(1, fresh.MinimumShopsToRemove);
        var page = await P0603Tests.ReadPreviewShopsAsync(p, term, fresh.PreviewId);
        Assert.Equal(3, page.Items.Length);
        Assert.Contains(page.Items, item => item.ShopId == addedShop);
        Assert.All(page.Items, item => Assert.Null(item.Retained));
        var schedule = await P0603Tests.ScheduleAsync(p, term, fresh, retained);
        Assert.Equal("Scheduled", schedule.State);
        Assert.Equal(2, schedule.RetainedShopCount);
        Assert.Equal(3, schedule.SourceShopCount);
        return schedule;
    }

    /// <summary>
    /// Known prerequisite gap (#911 merchant reduction fence × #912 slot assignment): the merchant fence requires every
    /// source shop to start at the term start, but a spare-slot shop starts at its own coverage instant, so the funded
    /// owner selection is held as NeedsReview/SourceChanged instead of applying. Pins the safe, recoverable outcome:
    /// no successor, no shop demoted or expired, one preserved payment and one finance review case.
    /// </summary>
    private static async Task AssertRenewalHeldForReviewAsync(P0308Fixture fixture, Package p, Guid term, Guid order,
        Guid slotShop)
    {
        var delivery = fixture.Checkout.Delivery;
        fixture.At(February29.AddTicks(-10));
        await P0603Tests.DrainReductionAsync(delivery);
        Assert.Equal("Funded", (await P0603Tests.ReadScheduleAsync(p, term)).State);
        fixture.At(February29);
        await P0603Tests.DrainReductionAsync(delivery, rounds: 4);
        var held = await P0603Tests.ReadScheduleAsync(p, term);
        Assert.Equal(("NeedsReview", "SourceChanged"), (held.State, held.Reason));
        Assert.Null(held.AppliedTermId);
        Assert.Equal(February14.UtcDateTime, (DateTime)(await delivery.MerchantScalarAsync(
            $"SELECT starts_at FROM shop_license_states WHERE shop_id='{slotShop:D}'"))!);
        Assert.Equal("NeedsReview/SourceChanged", await delivery.MerchantScalarAsync(
            "SELECT string_agg(outcome || '/' || reason, ',') FROM reduction_reservation_receipts"));
        Assert.Equal(1L, await fixture.CountAsync("subscription_coverage_terms"));
        Assert.Equal(3L, await fixture.CountAsync("payment_fulfilment_shops", $"term_id='{term:D}'"));
        Assert.Equal(0L, await delivery.MerchantScalarAsync("SELECT count(*) FROM applied_shop_reductions"));
        Assert.Equal(0L, await delivery.MerchantScalarAsync(
            $"SELECT count(*) FROM shop_license_states WHERE account_id='{p.Account:D}' AND state='Expired'"));
        Assert.Equal(1L, await fixture.CountAsync("verified_payments", $"order_id='{order:D}'"));
        Assert.Equal(0L, await fixture.CountAsync("payment_fulfilments", $"order_id='{order:D}'"));
        await AssertFinanceReviewCaseAsync(fixture, p.Account, order);
    }

    private static async Task AssertFinanceReviewCaseAsync(P0308Fixture fixture, Guid account, Guid order)
    {
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        using var reviews = await finance.SendAsync(HttpMethod.Get, $"/api/admin/accounts/{account:D}/payment-reviews");
        reviews.EnsureSuccessStatusCode();
        var reviewCase = Assert.Single((await reviews.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).EnumerateArray());
        Assert.Equal("SourceChanged", reviewCase.GetProperty("classification").GetString());
        Assert.Equal(order, reviewCase.GetProperty("orderId").GetGuid());
    }

    /// <summary>Every membership row of the package's shops and of the payer, byte for byte.</summary>
    private static async Task<string> RosterAsync(P0308Fixture fixture, Package p)
    {
        var shops = string.Join(',', p.Shops.Select(s => $"'{s.ShopId:D}'"));
        return (string)(await fixture.Checkout.Delivery.MerchantScalarAsync($"""
            SELECT jsonb_agg(to_jsonb(m) ORDER BY m.shop_id, m.user_id)::text FROM shop_memberships m
            WHERE m.shop_id IN ({shops}) OR m.user_id='{p.Payer.UserId:D}'
            """))!;
    }

    private static async Task<byte[]> ConsentReceiptAsync(P0308Fixture fixture, Guid shop) =>
        (byte[])(await fixture.Checkout.Delivery.MerchantScalarAsync(
            $"SELECT payload FROM entitlement_messages WHERE shop_id='{shop:D}' AND kind='CoverageSlotConsent'"))!;

    private static async Task<HttpResponseMessage> DeliverConsentAsync(P0213Fixture delivery, byte[] bytes)
    {
        using var signed = await delivery.Transport.SignedAsync(bytes, delivery.AuthClock, merchant: true,
            path: "/internal/licensing/v1/merchant-events/coverage-slot-consent");
        return await delivery.LicensingApi.SendAsync(signed);
    }
}
