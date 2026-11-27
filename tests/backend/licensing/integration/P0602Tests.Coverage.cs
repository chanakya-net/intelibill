using System.Net;
using System.Net.Http.Json;
using Intelibill.Domain.Enums;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0602Tests
{
    [Fact]
    public async Task CoverageFenceCountsPurchasedLinksAndPreservesNamedShopAuthority()
    {
        await using var fixture = await StartAsync(database, P0202Fixture.WholeSecondNow());
        var package = await fixture.PackageAsync(3);
        var now = P0202Fixture.WholeSecondNow().AddSeconds(1);
        fixture.At(now);
        fixture.Hosts.Fixture.Clock.Now = now;
        var (purchase, term) = await PurchaseAsync(package, "Month");
        var order = await IncreaseSeatsAsync(fixture, package, term, purchase.PreviewEndsAt);
        var delivery = fixture.Checkout.Delivery;
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);
        Assert.False(await SlotAvailableAsync(fixture, package, term)); // All three P03 links occupy slots.
        await PayAsync(package, order);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);
        Assert.False(await SlotAvailableAsync(fixture, package, term)); // Reservation grants no fourth slot.
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);
        Assert.True(await SlotAvailableAsync(fixture, package, term));
        Assert.Equal(4, (await AccountTermAsync(package, term)).GetProperty("purchasedCapacity").GetInt32());

        // P03's named-shop guard still applies. Assigning the fourth slot requires P06-04.
        var stranger = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Owner);
        using var refused = await package.Payer.Client.PostAsJsonAsync($"/api/billing/accounts/{package.Account}/affiliations", new
        {
            operationKey = Guid.NewGuid(), shopId = stranger.ShopId, withoutCoverage = false, termId = term,
        });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("Billing.AffiliationConflict", await P0206Fixture.CodeAsync(refused));
        Assert.Equal(3L, await fixture.CountAsync("payment_fulfilment_shops", $"term_id='{term}'"));
        Assert.Equal(0L, await fixture.CountAsync("shop_affiliations", $"term_id='{term}'"));
        Assert.Equal(1L, await delivery.MerchantScalarAsync($"SELECT count(*) FROM shop_memberships WHERE user_id='{package.Payer.UserId}'"));
    }

    private static async Task<bool> SlotAvailableAsync(P0308Fixture fixture, Package package, Guid term) =>
        (bool)(await fixture.ScalarAsync($"SELECT public.capacity_slot_available('{package.Account}','{term}',3)"))!;
}
