using System.Net.Http.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0604Tests
{
    [Fact]
    public async Task ShopJoiningAShorterPackageAfterAnEndedAnnualTermReadsItsEffectiveLink()
    {
        await using var fixture = await StartAsync(database, January31);
        var delivery = fixture.Checkout.Delivery;
        var (p, annual, monthly, named) = await SeedEndedAnnualBesideMonthlyPackageAsync(fixture);

        // The annual term's original end (January 2029) is later than the monthly package it now joins.
        SyncAt(fixture, February20);
        var assignment = await AcceptSlotAsync(p, monthly, named);
        await delivery.DrainAsync(false);
        Assert.Equal("Applied", await fixture.ScalarAsync($"SELECT state FROM coverage_slot_assignments WHERE assignment_id='{assignment}'"));

        var term = await ShopTermAsync(p, named);
        Assert.Equal(monthly, term.GetProperty("termId").GetGuid());
        Assert.Equal("Active", term.GetProperty("state").GetString());
        Assert.Equal(February20, term.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal(February29, term.GetProperty("paidThrough").GetDateTimeOffset());
        var listed = await p.Payer.Client.GetFromJsonAsync<CheckoutShopPageV1>(P0301Tests.OrdersPath(p.Account) + "/eligible-shops");
        Assert.Equal(monthly, Assert.Single(listed!.Items, shop => shop.ShopId == named.ShopId).TermId);

        var signed = await delivery.LatestSnapshotAsync(named.ShopId);
        Assert.Equal((monthly, "Active", assignment), (signed.Shop!.TermId!.Value, signed.Shop.State, signed.Shop.SlotAssignmentId!.Value));
        Assert.Equal((February20, February29.AddDays(7)), (signed.Shop.StartsAt!.Value, signed.Shop.AccessEndsAt!.Value));

        // First delivery and exact redelivery leave the merchant on the joined package.
        await DeliverSingleSnapshotAsync(fixture, signed.EventId, expectOutcome: "Applied");
        await DeliverSingleSnapshotAsync(fixture, signed.EventId, expectOutcome: "Applied");
        Assert.Equal(monthly, (Guid)(await delivery.MerchantScalarAsync(
            $"SELECT term_id FROM shop_license_states WHERE shop_id='{named.ShopId}'"))!);
        Assert.Equal(1L, await ActivationsAsync(fixture, named.ShopId));

        // One microsecond before the join the shop still reads the annual term up to its refunded end.
        SyncAt(fixture, February20.AddTicks(-10));
        var before = await ShopTermAsync(p, named);
        Assert.Equal(annual, before.GetProperty("termId").GetGuid());
        Assert.Equal(January31, before.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal(February20, before.GetProperty("paidThrough").GetDateTimeOffset());
    }

    private static async Task<(Package Package, Guid Annual, Guid Monthly, P0209Fixture.Merchant Named)>
        SeedEndedAnnualBesideMonthlyPackageAsync(P0308Fixture fixture)
    {
        var package = await fixture.PackageAsync(4, ["Central", "East", "North", "South"]);
        var named = package.Shops[2];
        var (annualPurchase, annual) = await PurchaseAsync(package with { Shops = [named] }, "Year");
        var (_, monthly) = await PurchaseAsync(package with { Shops = package.Shops[..2] }, "Month");
        await fixture.Checkout.Delivery.DrainAsync(true);
        await fixture.Checkout.Delivery.DrainAsync(false);

        SyncAt(fixture, February14);
        var adminClock = fixture.Checkout.Trial.Catalogue.Clock;
        P0609Tests.SetCredentialTime(fixture, adminClock.GetUtcNow());
        adminClock.Advanced += now => P0609Tests.SetCredentialTime(fixture, now);
        await ApproveRefundAsync(fixture, package, annualPurchase.OrderId, "EndAt", February20);
        await fixture.Checkout.Delivery.DrainAsync(true);
        await fixture.Checkout.Delivery.DrainAsync(false);
        return (package, annual, monthly, named);
    }
}
