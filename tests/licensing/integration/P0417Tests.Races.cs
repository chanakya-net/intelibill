using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0417Tests
{
    private static readonly DateTimeOffset CheckoutAt = new(2027, 3, 31, 4, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosureReviewAndConsentCannotCrossACompleteClosureCycle(bool deliveryBehind)
    {
        var clock = new P0201Clock { Now = P0202Fixture.WholeSecondNow() };
        await using var fixture = await P0213Fixture.StartAsync(database, businessClock: clock);
        var (owner, account) = await OpenShopAsync(fixture, clock.Now);
        using var manager = await SignInAsync(fixture);
        var proposal = await P0416Tests.ProposeAsync(owner, account);
        await P0416Tests.ConsentAsync(owner, proposal.GetProperty("id").GetGuid());

        var path = $"/api/licensing/shops/{owner.ShopId:D}/closure";
        var stale = await CloseCommandAsync(owner, path);
        var committed = stale with { OperationKey = Guid.NewGuid() };
        using var first = await owner.Client.PostAsJsonAsync(path, committed);
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        var id = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var route = $"/api/admin/accounts/{account:D}/closures/{id:D}/shops/{owner.ShopId:D}/reopening";
        using var reopened = await P0212Fixture.PostAsync(manager, route, Command(await PreviewAsync(manager, route), owner));
        Assert.True(reopened.StatusCode == HttpStatusCode.Created, await reopened.Content.ReadAsStringAsync());
        if (!deliveryBehind) await fixture.DrainAsync(source: true);

        using var staleAccount = await P0416Tests.ConfirmAccountAsync(owner, account, proposal);
        Assert.Equal(HttpStatusCode.Conflict, staleAccount.StatusCode);
        Assert.Equal("Closure.ReviewChanged", await P0206Fixture.CodeAsync(staleAccount));
        using var staleShop = await owner.Client.PostAsJsonAsync(path, stale);
        Assert.Equal("Closure.ReviewChanged", await P0206Fixture.CodeAsync(staleShop));
        using var replay = await owner.Client.PostAsJsonAsync(path, committed);
        Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
        Assert.Equal(id, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
    }

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

        await delivery.DrainAsync(source: true);
        await delivery.DrainAsync(source: false);
        // Retain a newer Closed snapshot whose own receipt may arrive after the reopening applies.
        await P0403Tests.QueueReconciliationAsync(delivery, package.Payer.ShopId);
        var pendingClosed = await delivery.LatestSnapshotAsync(package.Payer.ShopId);
        Assert.Equal("Closed", pendingClosed.Shop?.State);
        using var reopened = await P0212Fixture.PostAsync(manager, route, Command(await PreviewAsync(manager, route), package.Payer));
        Assert.True(reopened.StatusCode == HttpStatusCode.Created, await reopened.Content.ReadAsStringAsync());
        var reopenedSnapshot = await delivery.LatestSnapshotAsync(package.Payer.ShopId);
        var active = reopenedSnapshot.Shop!;
        Assert.Equal(retained.TermId, active.TermId);
        Assert.Equal(retained.AccessEndsAt, active.AccessEndsAt);
        P0302Fixture.AssertStatus(await P0302Fixture.StatusAsync(package.Payer.Client, quote), "Verified", "Fulfilled", "Applying");
        await AssertSnapshotDispositionAsync(delivery, reopenedSnapshot, "Applied");
        var olderReceipt = await P0213Tests.SendReceiptAsync(delivery, pendingClosed);
        Assert.Equal("Superseded", olderReceipt.Disposition);
        Assert.Equal(active.Revision, olderReceipt.AppliedShopRevision);
        using var signed = await delivery.Transport.SignedAsync(JsonSerializer.SerializeToUtf8Bytes(olderReceipt, EntitlementSnapshotsV1.Json),
            delivery.AuthClock, merchant: true, path: EntitlementSnapshotsV1.AcknowledgementPath);
        using var acceptedReceipt = await delivery.LicensingApi.SendAsync(signed);
        Assert.True(acceptedReceipt.IsSuccessStatusCode, await acceptedReceipt.Content.ReadAsStringAsync());
        P0302Fixture.AssertStatus(await P0302Fixture.StatusAsync(package.Payer.Client, quote), "Verified", "Fulfilled", "Applying");
        await delivery.DrainAsync(source: false);
        P0302Fixture.AssertStatus(await P0302Fixture.StatusAsync(package.Payer.Client, quote), "Verified", "Fulfilled", "Applied");
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OverlappingShopAndAccountClosuresRequireSeparateReviewsAndKeepSiblingsClosed(bool accountFirst)
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
        var accountImpact = await PreviewAsync(manager, route);
        Assert.Equal("Account", accountImpact.GetProperty("closureScope").GetString());
        Assert.Equal(1, accountImpact.GetProperty("otherActiveClosures").GetInt64());
        var shopImpact = await PreviewAsync(manager, shopClosure);
        var first = accountFirst ? (route, accountImpact) : (shopClosure, shopImpact);
        var second = accountFirst ? (shopClosure, shopImpact) : (route, accountImpact);
        await CheckOverlapDeliveryAsync(delivery, manager, package.Payer, first, second);
        Assert.Equal(untouched, await delivery.LatestSnapshotAsync(sibling.ShopId));
        var account = await package.Payer.Client.GetFromJsonAsync<JsonElement>($"/api/billing/accounts/{package.Account:D}/closures");
        Assert.Equal(2, account.GetProperty("closed").GetProperty("shops").GetArrayLength());
    }

    private static async Task CheckOverlapDeliveryAsync(P0213Fixture delivery, P0110CookieBrowser manager, Merchant owner,
        (string Route, JsonElement Impact) first, (string Route, JsonElement Impact) second)
    {
        await delivery.DrainAsync(source: true);
        await ActivateAsync(delivery, owner);
        var closed = await delivery.LatestSnapshotAsync(owner.ShopId);
        using var partialSaved = await P0212Fixture.PostAsync(manager, first.Route, Command(first.Impact, owner));
        Assert.True(partialSaved.StatusCode == HttpStatusCode.Created, await partialSaved.Content.ReadAsStringAsync());
        var partial = await delivery.LatestSnapshotAsync(owner.ShopId);
        Assert.Equal("Closed", partial.Shop?.State);
        Assert.Equal(closed.Shop!.ReopenedRevision, partial.Shop!.ReopenedRevision);
        await AssertMerchantStatusAsync(owner, received: false, enforced: null, partial: true);
        await AssertTradingHeldAsync(owner);
        await AssertSnapshotDispositionAsync(delivery, partial, "Applied");
        await AssertMerchantStatusAsync(owner, received: true, enforced: null, partial: true);
        await AssertTradingHeldAsync(owner);

        await AssertSnapshotDispositionAsync(delivery, partial with { EventId = Guid.NewGuid(),
            Shop = partial.Shop with { Revision = partial.Shop.Revision + 1, State = "Active" } }, "ConflictingShopScope");
        await AssertAfterMerchantRestartAsync(delivery, owner);

        using var stale = await P0212Fixture.PostAsync(manager, second.Route, Command(second.Impact, owner));
        Assert.Equal("Reopening.VersionConflict", await P0206Fixture.CodeAsync(stale));
        var remaining = await PreviewAsync(manager, second.Route);
        Assert.Equal(0, remaining.GetProperty("otherActiveClosures").GetInt64());
        using var saved = await P0212Fixture.PostAsync(manager, second.Route, Command(remaining, owner) with
        {
            OwnerConsentReference = "REVIEW-898-FINAL",
        });
        Assert.True(saved.StatusCode == HttpStatusCode.Created, await saved.Content.ReadAsStringAsync());
        await AssertMerchantStatusAsync(owner, received: false, enforced: false);
        var final = await delivery.LatestSnapshotAsync(owner.ShopId);
        Assert.Equal("Active", final.Shop?.State);
        Assert.Equal(partial.Shop.Revision, final.Shop?.ReopenedRevision);
        await AssertSnapshotDispositionAsync(delivery, final, "Applied");
        // Send the old Closed state with a new event ID to exercise reordered state, not receipt replay.
        await AssertSnapshotDispositionAsync(delivery, closed with { EventId = Guid.NewGuid() }, "Superseded");
        await AssertMerchantStatusAsync(owner, received: true, enforced: true);
    }

    private static async Task AssertSnapshotDispositionAsync(P0213Fixture delivery,
        Intelibill.Shared.Contracts.V1.EntitlementSnapshotV1 snapshot, string disposition)
    {
        var (http, receipt) = await delivery.SendAsync(snapshot);
        using (http)
            Assert.Equal(disposition, receipt?.Disposition);
    }

    private static async Task AssertAfterMerchantRestartAsync(P0213Fixture delivery, Merchant owner)
    {
        await using var restarted = delivery.Hosts.Merchant.WithWebHostBuilder(_ => { });
        using var client = restarted.CreateClient();
        client.DefaultRequestHeaders.Authorization = owner.Client.DefaultRequestHeaders.Authorization;
        var merchant = owner with { Client = client };
        await AssertMerchantStatusAsync(merchant, received: true, enforced: null, partial: true);
        await AssertTradingHeldAsync(merchant);
    }
}
