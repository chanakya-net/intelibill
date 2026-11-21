using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0417Tests
{
    private static readonly DateTimeOffset CheckoutAt = new(2027, 3, 31, 4, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task PaymentOnAClosedShopRequiresANewReviewEvenWhenItRacesConfirmation()
    {
        await using var checkout = await P0308Fixture.StartAsync(database, CheckoutAt, P0306Tests.Settings);
        var package = await checkout.PackageAsync(1);
        var quote = await P0308Fixture.AcceptAsync(package, await P0308Fixture.FirstQuoteAsync(package, "Month"));
        var attempt = await P0308Fixture.StartPaymentAsync(package, quote);
        await P0302Fixture.CaptureAsync(package.Payer.Client, quote, attempt);
        var delivery = checkout.Checkout.Delivery;
        var route = await CloseShopAsync(package.Payer, package.Account);
        using var manager = await SignInAsync(delivery);
        var before = Command(await PreviewAsync(manager, route), package.Payer);

        var confirmation = P0212Fixture.PostAsync(manager, route, before);
        var payment = P0302Fixture.VerifyAsync(package.Payer.Client, quote, attempt);
        await Task.WhenAll(confirmation, payment);
        using var refused = await confirmation;
        var code = await P0206Fixture.CodeAsync(refused);
        Assert.True(code is "Reopening.VersionConflict" or "Reopening.CoverageRequired", code);
        Assert.Equal(0L, await checkout.CountAsync("operational_closure_reopenings"));
        var paid = await payment;
        P0302Fixture.AssertStatus(paid, "Verified", "Fulfilled", "NeedsReview");
        var retained = (await delivery.LatestSnapshotAsync(package.Payer.ShopId)).Shop!;
        Assert.Equal("Closed", retained.State);

        using var reopened = await P0212Fixture.PostAsync(manager, route, Command(await PreviewAsync(manager, route), package.Payer));
        Assert.True(reopened.StatusCode == HttpStatusCode.Created, await reopened.Content.ReadAsStringAsync());
        var active = (await delivery.LatestSnapshotAsync(package.Payer.ShopId)).Shop!;
        Assert.Equal(retained.TermId, active.TermId);
        Assert.Equal(retained.AccessEndsAt, active.AccessEndsAt);
        Assert.Equal(1L, await checkout.CountAsync("payment_fulfilments"));
        Assert.Equal(0L, await checkout.CountAsync("trial_history"));
    }

    [Fact]
    public async Task ReopeningAndAccountClosureCannotCommitTheSameReviewedClosureScope()
    {
        var clock = new P0201Clock { Now = P0202Fixture.WholeSecondNow() };
        await using var fixture = await P0213Fixture.StartAsync(database, businessClock: clock);
        var (owner, account, route) = await CloseAsync(fixture, clock.Now);
        await fixture.DrainAsync(source: true);
        var proposal = await P0416Tests.ProposeAsync(owner, account);
        await P0416Tests.ConsentAsync(owner, proposal.GetProperty("id").GetGuid());
        using var manager = await SignInAsync(fixture);
        var command = Command(await PreviewAsync(manager, route), owner);

        var reopening = P0212Fixture.PostAsync(manager, route, command);
        var closure = P0416Tests.ConfirmAccountAsync(owner, account, proposal);
        await Task.WhenAll(reopening, closure);
        using var reopened = await reopening;
        using var closed = await closure;
        if (reopened.StatusCode == HttpStatusCode.Created)
        {
            Assert.Equal("Closure.ReviewChanged", await P0206Fixture.CodeAsync(closed));
            Assert.Equal(1L, await fixture.Trial.CountAsync("operational_closure_reopenings"));
            Assert.Equal("Active", (await fixture.LatestSnapshotAsync(owner.ShopId)).Shop?.State);
        }
        else
        {
            Assert.True(closed.IsSuccessStatusCode, await closed.Content.ReadAsStringAsync());
            Assert.Equal("Reopening.VersionConflict", await P0206Fixture.CodeAsync(reopened));
            Assert.Equal(0L, await fixture.Trial.CountAsync("operational_closure_reopenings"));
            Assert.Equal("Closed", (await fixture.LatestSnapshotAsync(owner.ShopId)).Shop?.State);
        }
    }

    [Fact]
    public async Task OverlappingShopAndAccountClosuresRequireSeparateReviewsAndKeepSiblingsClosed()
    {
        await using var checkout = await P0308Fixture.StartAsync(database, CheckoutAt, P0306Tests.Settings);
        var package = await checkout.PackageAsync(2);
        await P0308Fixture.PurchaseAsync(package, "Month");
        var delivery = checkout.Checkout.Delivery;
        await delivery.DrainAsync(source: true);
        var shopClosure = await CloseShopAsync(package.Payer, package.Account);
        await delivery.DrainAsync(source: true);
        var proposal = await P0416Tests.ProposeAsync(package.Payer, package.Account);
        var proposalId = proposal.GetProperty("id").GetGuid();
        foreach (var shop in package.Shops) await P0416Tests.ConsentAsync(shop, proposalId);
        using var closed = await P0416Tests.ConfirmAccountAsync(package.Payer, package.Account, proposal);
        Assert.True(closed.IsSuccessStatusCode, await closed.Content.ReadAsStringAsync());
        var closureId = (await closed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var sibling = package.Shops[1];
        var untouched = await delivery.LatestSnapshotAsync(sibling.ShopId);
        Assert.Equal("Closed", untouched.Shop?.State);
        var route = $"/api/admin/accounts/{package.Account:D}/closures/{closureId:D}/shops/{package.Payer.ShopId:D}/reopening";
        using var manager = await SignInAsync(delivery);
        var impact = await PreviewAsync(manager, route);
        Assert.Equal("Account", impact.GetProperty("closureScope").GetString());
        Assert.Equal(1, impact.GetProperty("otherActiveClosures").GetInt64());
        var shopImpact = await PreviewAsync(manager, shopClosure);
        using var first = await P0212Fixture.PostAsync(manager, shopClosure, Command(shopImpact, package.Payer));
        Assert.True(first.StatusCode == HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        Assert.Equal("Closed", (await delivery.LatestSnapshotAsync(package.Payer.ShopId)).Shop?.State);
        using var stale = await P0212Fixture.PostAsync(manager, route, Command(impact, package.Payer));
        Assert.Equal("Reopening.VersionConflict", await P0206Fixture.CodeAsync(stale));
        var remaining = await PreviewAsync(manager, route);
        Assert.Equal(0, remaining.GetProperty("otherActiveClosures").GetInt64());
        using var saved = await P0212Fixture.PostAsync(manager, route, Command(remaining, package.Payer) with
        {
            OwnerConsentReference = "REVIEW-898-ACCOUNT",
        });
        Assert.True(saved.StatusCode == HttpStatusCode.Created, await saved.Content.ReadAsStringAsync());
        Assert.Equal(untouched, await delivery.LatestSnapshotAsync(sibling.ShopId));
        Assert.Equal("Active", (await delivery.LatestSnapshotAsync(package.Payer.ShopId)).Shop?.State);

        var account = await package.Payer.Client.GetFromJsonAsync<JsonElement>($"/api/billing/accounts/{package.Account:D}/closures");
        Assert.Equal(2, account.GetProperty("closed").GetProperty("shops").GetArrayLength());
    }
}
