using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0602Tests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProviderSeesFinalManualDecision(bool expire)
    {
        await using var fixture = await StartAsync(database, January31, P0306Tests.Settings);
        var (package, _, path, request) = await PreviewAsync(fixture);
        var order = await ConvertAsync(package, path, request);
        var delivery = fixture.Checkout.Delivery;
        await delivery.ScanAsync(true);
        await ExchangeAsync(delivery);
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        var receipts = P0306Tests.Path(order);
        using var created = await P0212Fixture.PostAsync(finance, receipts, P0306Tests.Receipt(order, February14));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var receipt = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receiptId").GetGuid();
        await ExchangeAsync(delivery);
        if (expire) fixture.At(February14.AddMinutes(5));
        await delivery.DrainAsync(true);
        await delivery.DeliverAsync(false);
        using var read = await finance.SendAsync(HttpMethod.Get, receipts);
        read.EnsureSuccessStatusCode();
        var view = await read.Content.ReadFromJsonAsync<JsonElement>();
        var saved = Assert.Single(view.GetProperty("receipts").EnumerateArray());
        Assert.Equal(expire ? "NeedsReview" : "Fulfilled", saved.GetProperty("fulfilment").GetString());
        using var reviews = await finance.SendAsync(HttpMethod.Get, $"/api/admin/accounts/{package.Account}/payment-reviews");
        reviews.EnsureSuccessStatusCode();
        var cases = (await reviews.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToArray();
        if (!expire) { Assert.Empty(cases); return; }
        var item = Assert.Single(cases);
        Assert.Equal(order.OrderId, item.GetProperty("orderId").GetGuid());
        Assert.Equal(receipt, item.GetProperty("manualReceiptId").GetGuid());
        Assert.Equal("ReservationExpired", item.GetProperty("classification").GetString());
    }

    [Fact]
    public async Task SecondIncreaseUsesAppliedPriceAndSeatsOnOriginalTerm()
    {
        await using var fixture = await StartAsync(database, P0202Fixture.WholeSecondNow());
        var package = await fixture.PackageAsync(2);
        var now = P0202Fixture.WholeSecondNow().AddSeconds(1);
        fixture.At(now);
        fixture.Hosts.Fixture.Clock.Now = now;
        var (purchase, term) = await PurchaseAsync(package, "Month");
        var delivery = fixture.Checkout.Delivery;
        var first = await IncreaseSeatsAsync(fixture, package, term, purchase.PreviewEndsAt);
        await ExchangeAsync(delivery);
        await PayAsync(package, first);
        await ExchangeAsync(delivery);
        await ExchangeAsync(delivery);
        await ExchangeAsync(delivery);
        var plan = (Guid)(await fixture.ScalarAsync($"SELECT plan_id FROM plan_versions WHERE id='{package.Plan}'"))!;
        var next = await P0301Tests.PublishPlanAsync(fixture.Checkout.Trial, plan, first.PlanVersionId, 7, 24000);
        using var preview = await PostQuoteAsync(package, term, Request(next, 5, 4, purchase.PreviewEndsAt));
        preview.EnsureSuccessStatusCode();
        var quote = await preview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4, quote.GetProperty("priorCapacity").GetInt32());
        Assert.Equal(6, quote.GetProperty("priorMemberAllowance").GetInt32());
        Assert.Equal(33000, quote.GetProperty("purchasedGrossPaise").GetInt64());
        var order = await ConvertAsync(package, $"{QuotesPath(package.Account, term)}/{quote.GetProperty("quoteId").GetGuid()}/order",
            new { operationKey = Guid.NewGuid(), snapshotHash = quote.GetProperty("snapshotHash").GetString() });
        await PayAsync(package, order);
        await ExchangeAsync(delivery);
        await ExchangeAsync(delivery);
        Assert.Equal(1L, await fixture.CountAsync("capacity_increases", $"order_id='{order.OrderId}' AND state='Applied' AND target_revision=2"));
        var visible = await AccountTermAsync(package, term);
        Assert.Equal(purchase.PreviewEndsAt, visible.GetProperty("paidThrough").GetDateTimeOffset());
        Assert.Equal(5, visible.GetProperty("purchasedCapacity").GetInt32());
        Assert.Equal(7, visible.GetProperty("memberAllowance").GetInt32());
        var shop = package.Shops[1];
        var staff = await StaffAsync(fixture, shop.ShopId, 5);
        await LicensingPostgreSqlFixture.ExecuteAsync(delivery.MerchantDb,
            $"UPDATE shop_license_states SET enforcement_activated_at=now() WHERE shop_id='{shop.ShopId}'");
        using var finalSeat = await ActivateAsync(shop, staff[0]);
        finalSeat.EnsureSuccessStatusCode();
        using var excess = await ActivateAsync(shop, staff[1]);
        Assert.Equal("Licence.MemberCapacityReached", await P0206Fixture.CodeAsync(excess));
    }
}
