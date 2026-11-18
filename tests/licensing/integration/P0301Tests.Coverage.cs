using System.Net;
using System.Net.Http.Json;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Domain.ValueObjects;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0301Tests
{
    [Fact]
    public async Task QuotePagesEveryShopInAGroupLargerThanOneHundred()
    {
        await using var fixture = await StartCheckoutAsync(database, new P0201Clock { Now = P0210Fixture.Start });
        var payer = await SignInAsync(fixture.Hosts);
        var account = await CreateAccountAsync(payer.Client);
        var shops = Enumerable.Range(0, 105).Select(_ => Guid.NewGuid()).Order().ToArray();
        await SeedShopsAsync(fixture, payer, account, shops);
        var version = await PublishPlanAsync(fixture);
        using var clientPrice = await payer.Client.PostAsJsonAsync(OrdersPath(account), new
        {
            operationKey = Guid.NewGuid(), planVersionId = version, period = "Month", shopIds = shops,
            extraShops = 102, expectedRevision = 1, amountPaise = 1,
        });
        Assert.Equal(HttpStatusCode.BadRequest, clientPrice.StatusCode);
        Assert.Equal(0L, await fixture.CountAsync("checkout_orders"));
        var quote = await CreateQuoteAsync(payer.Client, account, new(Guid.NewGuid(), version, "Month", shops, 102, 1));
        Assert.Equal(105, quote.ShopCount);
        Assert.Equal(105, quote.Capacity);
        Assert.Equal(1_542_000, quote.AmountPaise);
        Assert.Equal(CheckoutOrdersV1.ShopHash(shops), quote.ShopHash);
        Assert.Equal(shops, await EveryShopAsync(payer, account, $"/{quote.OrderId:D}/shops"));
        Assert.Equal(shops, await EveryShopAsync(payer, account, "/eligible-shops"));
        using var accept = await payer.Client.PostAsJsonAsync(AcceptPath(quote), Accept(quote));
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
    }

    [Fact]
    public async Task WholeRenewalUsesPriorAnchorAndRefusesChangedCoverage()
    {
        var anchor = new BillingAnchor(new DateTime(2027, 1, 31, 10, 0, 0), BillingCalendar.Zone, BillingPeriod.Month);
        var (starts, ends) = BillingCalendar.Period(anchor, 0);
        var clock = new P0201Clock { Now = ends.AddDays(1) };
        await using var fixture = await StartCheckoutAsync(database, clock);
        var payer = await SignInAsync(fixture.Hosts);
        var account = await CreateAccountAsync(payer.Client);
        var term = await SeedTermAsync(fixture.Hosts, account, new(starts, ends, AnchorLocal: "2027-01-31 10:00:00",
            Grouped: true, Capacity: 3));
        var shop = Guid.NewGuid();
        await AffiliateAsync(fixture.LicensingOwner, account, payer.UserId, shop, payer.UserId, term, starts, ends);
        var version = await PublishPlanAsync(fixture);
        var command = new CreateCheckoutQuoteV1(Guid.NewGuid(), version, "Month", [], 0, 1, term);
        var quote = await CreateQuoteAsync(payer.Client, account, command);
        Assert.Equal("PriorPaidThrough", quote.StartRule);
        Assert.Equal(ends, quote.PreviewStartsAt);
        Assert.Equal(BillingCalendar.Boundary(anchor, 2), quote.PreviewEndsAt);
        Assert.Equal(1, quote.GraceDaysIncluded);
        Assert.Equal(3, quote.Capacity);
        Assert.Equal(1, quote.ShopCount);
        await AffiliateAsync(fixture.LicensingOwner, account, payer.UserId, Guid.NewGuid(), payer.UserId, term, starts, ends);
        using var conflicted = await payer.Client.PostAsJsonAsync(AcceptPath(quote), Accept(quote));
        Assert.Equal(HttpStatusCode.Conflict, conflicted.StatusCode);
        Assert.Equal(CheckoutOrdersV1.SourceChanged, await P0206Fixture.CodeAsync(conflicted));
        Assert.Equal(0L, await fixture.CountAsync("checkout_order_acceptances"));
        Assert.Equal(1L, await fixture.CountAsync("subscription_term_facts"));
    }

    private static async Task SeedShopsAsync(P0210Fixture fixture, Merchant payer, Guid account, Guid[] shops)
    {
        foreach (var shop in shops)
            await AffiliateAsync(fixture.LicensingOwner, account, payer.UserId, shop, payer.UserId, null, P0210Fixture.Start.AddDays(-1), null);
    }

    private static async Task<Guid[]> EveryShopAsync(Merchant payer, Guid account, string suffix)
    {
        var ids = new List<Guid>();
        Guid? cursor = null;
        do
        {
            var query = cursor is { } after ? $"?after={after:D}" : "";
            var page = await payer.Client.GetFromJsonAsync<CheckoutShopPageV1>(OrdersPath(account) + suffix + query);
            Assert.NotNull(page);
            Assert.InRange(page.Items.Length, 1, CheckoutOrdersV1.PageSize);
            ids.AddRange(page.Items.Select(s => s.ShopId));
            Assert.True(page.NextCursor is null || page.NextCursor != cursor);
            cursor = page.NextCursor;
        } while (cursor is not null);
        return [.. ids];
    }
}
