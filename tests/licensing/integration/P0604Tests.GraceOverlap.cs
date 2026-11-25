using System.Net;
using System.Net.Http.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0604Tests
{
    private static readonly DateTimeOffset GraceEnd = February29.AddDays(7);

    [Fact]
    public async Task NamedGrantInsideTheCandidateGraceWindowRefusesPreviewRequestAndDelayedReceipt()
    {
        await using var f = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(f);
        var (north, south) = (p.Shops[2], p.Shops[3]);
        var assignment = await AcceptSlotAsync(p, term, north);
        var consent = (byte[])(await f.Checkout.Delivery.MerchantScalarAsync(
            $"SELECT payload FROM entitlement_messages WHERE shop_id='{north.ShopId}' AND kind='CoverageSlotConsent'"))!;

        // The grant starts after paid-through (29 February) yet inside the access the slot would carry (to 7 March).
        await P0212Fixture.GrantAsync(f.Checkout.Trial.CatalogueManager!, p.Account,
            P0212Fixture.Draft([north.ShopId, south.ShopId], February29.AddDays(1), February29.AddDays(4)));

        var preview = await p.Payer.Client.GetFromJsonAsync<CoverageSlotTermV1>(Slots(p, term));
        Assert.DoesNotContain(preview!.EligibleShops, shop => shop.ShopId == south.ShopId);
        using var refused = await p.Payer.Client.PostAsJsonAsync(Slots(p, term), Request(south.ShopId, Guid.NewGuid()));
        Assert.Equal("Coverage.SlotShopIneligible", await P0206Fixture.CodeAsync(refused));

        await f.Checkout.Delivery.DrainAsync(false);
        Assert.Equal("ShopCovered", await f.ScalarAsync($"SELECT reason FROM coverage_slot_assignments WHERE assignment_id='{assignment}'"));
        using var redelivery = await DeliverConsentAsync(f.Checkout.Delivery, consent);
        Assert.Equal(HttpStatusCode.NoContent, redelivery.StatusCode);
        Assert.Equal("Refused", await f.ScalarAsync($"SELECT state FROM coverage_slot_assignments WHERE assignment_id='{assignment}'"));
        Assert.Equal(2L, await f.CountAsync("payment_fulfilment_shops", $"term_id='{term}'"));
        Assert.Equal(0L, await f.CountAsync("coverage_slot_assignment_deliveries"));
        Assert.Equal(1L, await f.CountAsync("coverage_slot_assignments"));
    }

    [Fact]
    public async Task GrantStartingExactlyAtTheFinalAccessEndStaysAdjacent()
    {
        await using var f = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(f);
        var north = p.Shops[2];
        var assignment = await AcceptSlotAsync(p, term, north);
        await P0212Fixture.GrantAsync(f.Checkout.Trial.CatalogueManager!, p.Account,
            P0212Fixture.Draft([north.ShopId], GraceEnd, GraceEnd.AddDays(3)));
        await f.Checkout.Delivery.DrainAsync(false);
        Assert.Equal("Applied", await f.ScalarAsync($"SELECT state FROM coverage_slot_assignments WHERE assignment_id='{assignment}'"));
        Assert.Equal(3L, await f.CountAsync("payment_fulfilment_shops", $"term_id='{term}'"));
    }

    [Theory]
    [InlineData(0, "Applied")]
    [InlineData(-1, "Refused")]
    public async Task RefundTreatmentWithoutGraceBoundsTheOverlapAtItsAccessEnd(int grantOffsetHours, string expected)
    {
        await using var f = await StartAsync(database, January31);
        var (p, orderId, term) = await PrepareRefundableAsync(f);
        var north = p.Shops[2];
        await ApproveRefundAsync(f, p, orderId, "EndAt", February20);
        using var created = await p.Payer.Client.PostAsJsonAsync(Slots(p, term), new
        {
            operationKey = Guid.NewGuid(), shopId = north.ShopId, observedCapacity = 3, observedPaidThrough = February20,
            acknowledgedSharedEnd = true, acknowledgedNoNewFee = true,
        });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var assignment = (await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("assignmentId").GetGuid();
        var path = Consent(p, north.ShopId, assignment);
        (await Decide(north.Client, path, "grant")).EnsureSuccessStatusCode();
        (await Decide(north.Client, path, "accept")).EnsureSuccessStatusCode();

        // EndAt leaves no grace: access ends with paid-through on 20 February.
        var starts = February20.AddHours(grantOffsetHours);
        await P0212Fixture.GrantAsync(f.Checkout.Trial.CatalogueManager!, p.Account,
            P0212Fixture.Draft([north.ShopId], starts, starts.AddDays(3)));
        await f.Checkout.Delivery.DrainAsync(false);
        Assert.Equal(expected, await f.ScalarAsync($"SELECT state FROM coverage_slot_assignments WHERE assignment_id='{assignment}'"));
        Assert.Equal(expected == "Applied" ? 3L : 2L, await f.CountAsync("payment_fulfilment_shops", $"term_id='{term}'"));
    }
}
