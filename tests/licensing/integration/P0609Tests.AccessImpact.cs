using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0609Tests
{
    [Fact]
    public async Task RefundPreviewDistinguishesCoverageFromRestrictionClosureAndCourtesy()
    {
        await using var fixture = await StartAsync(database, Instant);
        var (package, named, _, request) = await TwoSubscriptionsAsync(fixture);
        var delivery = fixture.Checkout.Delivery;
        var seed = await P0306Tests.EnrollBillingAsync(fixture.Checkout, P0306Tests.BillingEmail);
        using var security = await P0417Tests.SignInAsync(delivery, "security-administrator");
        using var manager = await fixture.Checkout.Trial.Catalogue.SignInOperatorAsync(
            fixture.Checkout.Trial.CatalogueManagerSeed!, P0114Tests.Invitee);
        using var billing = await fixture.Checkout.Trial.Catalogue.SignInOperatorAsync(seed, P0306Tests.BillingEmail);
        var scheduled = Instant.AddMinutes(1);
        await P0403Tests.ApplySuspensionAsync(security, package.Account, "Shop", named.Shops[0].ShopId, "Trading", scheduled);
        await P0212Fixture.GrantAsync(manager, package.Account, P0212Fixture.Draft([named.Shops[1].ShopId],
            Instant, Boundary.AddDays(1)));
        var before = await PreviewAsync(billing, package.Account, request, 4500, "EndAt", Boundary);
        var shops = before.GetProperty("access").GetProperty("shops").EnumerateArray().ToDictionary(s => s.GetProperty("shopId").GetGuid());
        Assert.False(shops[named.Shops[0].ShopId].GetProperty("restricted").GetBoolean());
        Assert.Equal(scheduled, shops[named.Shops[0].ShopId].GetProperty("scheduledRestrictionAt").GetDateTimeOffset());
        Assert.Equal(Boundary.AddDays(1), shops[named.Shops[1].ShopId].GetProperty("courtesyEndsAt").GetDateTimeOffset());
        Assert.Equal("Active", (await TermAsync(named.Shops[0])).GetProperty("state").GetString());
        await P0417Tests.CloseShopAsync(named.Shops[0], package.Account);
        await AssertCodeAsync(await ApproveAsync(billing, package.Account, request, Command(before, 4500, "EndAt", Boundary)),
            HttpStatusCode.Conflict, "Refund.PreviewStale");
        fixture.At(scheduled);
        var latest = await PreviewAsync(billing, package.Account, request, 4500, "EndAt", Boundary);
        var closed = latest.GetProperty("access").GetProperty("shops").EnumerateArray()
            .Single(s => s.GetProperty("shopId").GetGuid() == named.Shops[0].ShopId);
        Assert.True(closed.GetProperty("closed").GetBoolean());
        Assert.True(closed.GetProperty("restricted").GetBoolean());
        await ReadAsync(await ApproveAsync(billing, package.Account, request, Command(latest, 4500, "EndAt", Boundary)),
            HttpStatusCode.Created);
        fixture.At(Boundary);
        Assert.Equal("Closed", (await delivery.LatestSnapshotAsync(named.Shops[0].ShopId)).Shop!.State);
        Assert.Equal("Complimentary", (await TermAsync(named.Shops[1])).GetProperty("basis").GetString());
    }

    [Fact]
    public async Task AdjustedImportedTermRejectsOldCancellationAndPendingShopConsent()
    {
        var now = P0202Fixture.WholeSecondNow().AddSeconds(1);
        await using var fixture = await StartAsync(database, now, P0306Tests.Settings);
        fixture.Hosts.Fixture.Clock.Now = now;
        var owner = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Owner);
        var extra = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await P0209Fixture.CreateAccountAsync(owner.Client);
        var plan = await P0301Tests.PublishPlanAsync(fixture.Checkout.Trial);
        var start = now.AddDays(-1);
        var end = start.AddMonths(1);
        var term = await P0209Fixture.SeedTermAsync(fixture.Hosts, account, new(start, end,
            AnchorLocal: start.ToOffset(TimeSpan.FromHours(5.5)).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            PricePaise: 12000, Grouped: true, Capacity: 3, PlanVersionId: plan));
        await P0213Fixture.LinkAsync(account, owner, owner, term);
        var package = new Package(owner, account, [owner], plan);
        var cancelPath = P0215Tests.CancellationPath(account, term);
        var cancellation = await P0215Fixture.PreviewAsync(owner, cancelPath);
        var consent = await P0215Fixture.RequestAsync(owner, extra, account, term);
        Assert.Equal("Granted", await P0205Tests.DecideAsync(extra.Client, consent, "grant"));
        var order = await P0602Tests.IncreaseSeatsAsync(fixture, package, term, end);
        await fixture.Checkout.Delivery.ScanAsync(true);
        await P0608Tests.DrainAsync(fixture);
        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        await ReadAsync(await P0212Fixture.PostAsync(billing, P0306Tests.Path(order), P0306Tests.Receipt(order, now)),
            HttpStatusCode.Created);
        await P0608Tests.DrainAsync(fixture);
        await P0608Tests.DrainAsync(fixture);
        Assert.Equal(1L, await fixture.CountAsync("capacity_increases", "state='Applied'"));
        var confirmation = await P0608Tests.ConfirmationIdAsync(owner.Client, account, order.OrderId);
        var request = await RequestRefundAsync(package, order.OrderId, confirmation, order.AmountPaise);
        var boundary = now.AddMinutes(1);
        var preview = await PreviewAsync(billing, account, request, 100, "EndAt", boundary);
        await ReadAsync(await ApproveAsync(billing, account, request, Command(preview, 100, "EndAt", boundary)), HttpStatusCode.Created);
        fixture.At(boundary);
        fixture.Hosts.Fixture.Clock.Now = boundary;
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.GetAsync(cancelPath)).StatusCode);
        using var stale = await owner.Client.PostAsJsonAsync(cancelPath, P0215Tests.Accept(cancellation, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var list = await owner.Client.GetFromJsonAsync<JsonElement>($"/api/billing/accounts/{account}/affiliations");
        Assert.Empty(list.GetProperty("terms").EnumerateArray());
        Assert.Equal("Accepted", await P0205Tests.DecideAsync(extra.Client, consent, "accept"));
        await P0205Tests.WaitForConsentAsync(extra.Client, consent, "NeedsReview");
        Assert.Equal(0L, await fixture.CountAsync("shop_affiliations", $"shop_id='{extra.ShopId}'"));
    }
}
