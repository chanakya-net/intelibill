using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0604Tests
{
    internal static readonly DateTimeOffset March1 = P0209Fixture.Utc("2028-03-01T04:30:00Z");
    internal static readonly DateTimeOffset March10 = P0209Fixture.Utc("2028-03-10T04:30:00Z");
    internal static readonly DateTimeOffset April1 = P0209Fixture.Utc("2028-04-01T04:30:00Z");

    private static async Task<(Package Package, Guid Term1, Guid Term2, P0209Fixture.Merchant Named)>
        SeedTwoIndependentTermsAsync(P0308Fixture fixture)
    {
        var package = await fixture.PackageAsync(4, ["Central", "East", "North", "South"]);
        var named = package.Shops[2];
        // Term 1 covers named shop [Jan 31, Feb 29), grace ends Mar 7.
        var (_, term1) = await PurchaseAsync(package with { Shops = [named] }, "Month");
        await fixture.Checkout.Delivery.DrainAsync(true);
        await fixture.Checkout.Delivery.DrainAsync(false);

        // Term 2 is an independently dated grouped package [Mar 1, Apr 1) with 1 spare slot.
        SyncAt(fixture, March1);
        var (_, term2) = await PurchaseAsync(package with { Shops = package.Shops[..2] }, "Month");
        await fixture.Checkout.Delivery.DrainAsync(true);
        await fixture.Checkout.Delivery.DrainAsync(false);

        // Move past Term 1 paid-through + 7-day grace (Mar 7) -> Mar 10.
        SyncAt(fixture, March10);
        await fixture.Checkout.Delivery.ScanAsync(true);
        await fixture.Checkout.Delivery.DrainAsync(true);
        await fixture.Checkout.Delivery.DrainAsync(false);
        return (package, term1, term2, named);
    }

    [Fact]
    public async Task RestoringExpiredIndependentTermCannotOverlapAfterSlotAssignment()
    {
        await using var fixture = await StartAsync(database, January31);
        var (p, term1, term2, named) = await SeedTwoIndependentTermsAsync(fixture);

        // Pre-create and accept TWO restoration quotes for Term 1 BEFORE the slot assignment commits:
        // one already captured (must land in NeedsReview) and one merely accepted (must be refused at payment start).
        var acceptedQuote = await AcceptAsync(p, await RenewalQuoteAsync(p, term1, "Month"));
        Assert.Equal("RestoredAccess", acceptedQuote.StartRule);
        var attempt = await StartPaymentAsync(p, acceptedQuote);
        await P0302Fixture.CaptureAsync(p.Payer.Client, acceptedQuote, attempt);
        var unstartedQuote = await AcceptAsync(p, await RenewalQuoteAsync(p, term1, "Month"));

        // Commit order 1: assign named shop to Term 2 FIRST (using Term 2's April 1 shared end).
        var slotReq = new
        {
            operationKey = Guid.NewGuid(), shopId = named.ShopId, observedCapacity = 3,
            observedPaidThrough = April1, acknowledgedSharedEnd = true, acknowledgedNoNewFee = true,
        };
        using var created = await p.Payer.Client.PostAsJsonAsync(Slots(p, term2), slotReq);
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var assignmentId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("assignmentId").GetGuid();
        var consentPath = Consent(p, named.ShopId, assignmentId);
        (await Decide(named.Client, consentPath, "grant")).EnsureSuccessStatusCode();
        (await Decide(named.Client, consentPath, "accept")).EnsureSuccessStatusCode();
        await fixture.Checkout.Delivery.DrainAsync(false);
        Assert.Equal("Applied", await fixture.ScalarAsync($"SELECT state FROM coverage_slot_assignments WHERE assignment_id='{assignmentId}'"));

        // A brand-new restoration quote AND starting payment on the already-accepted quote are both refused.
        using var newQuote = await TryRenewalQuoteAsync(p, term1, "Month");
        Assert.False(newQuote.IsSuccessStatusCode);
        await P0302Fixture.StartAsync(p.Payer.Client, unstartedQuote, HttpStatusCode.Conflict);

        // Already-captured restoration order for Term 1 retains verified funds in NeedsReview without creating overlapping coverage.
        var verified = await P0302Fixture.VerifyAsync(p.Payer.Client, acceptedQuote, attempt);
        P0302Fixture.AssertStatus(verified, "Verified", "NeedsReview", "NotApplicable");
        // Replay does not extend or duplicate anything.
        var replayed = await P0302Fixture.VerifyAsync(p.Payer.Client, acceptedQuote, attempt);
        P0302Fixture.AssertStatus(replayed, "Verified", "NeedsReview", "NotApplicable");

        Assert.Equal(0L, await fixture.CountAsync("payment_fulfilments", $"predecessor_term_id='{term1}'"));
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilment_shops", $"shop_id='{named.ShopId}' AND term_id='{term2}'"));
        Assert.Equal(term2, (await ShopTermAsync(p, named)).GetProperty("termId").GetGuid());
    }

    [Fact]
    public async Task SlotAssignmentRefusesWhenExpiredTermRestoresFirst()
    {
        await using var fixture = await StartAsync(database, January31);
        var (p, term1, term2, named) = await SeedTwoIndependentTermsAsync(fixture);

        // Create and accept the slot assignment for Term 2, holding its consent message before delivery.
        var slotReq = new
        {
            operationKey = Guid.NewGuid(), shopId = named.ShopId, observedCapacity = 3,
            observedPaidThrough = April1, acknowledgedSharedEnd = true, acknowledgedNoNewFee = true,
        };
        using var created = await p.Payer.Client.PostAsJsonAsync(Slots(p, term2), slotReq);
        created.EnsureSuccessStatusCode();
        var assignmentId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("assignmentId").GetGuid();
        var consentPath = Consent(p, named.ShopId, assignmentId);
        (await Decide(named.Client, consentPath, "grant")).EnsureSuccessStatusCode();
        (await Decide(named.Client, consentPath, "accept")).EnsureSuccessStatusCode();
        var bytes = (byte[])(await fixture.Checkout.Delivery.MerchantScalarAsync(
            $"SELECT payload FROM entitlement_messages WHERE shop_id='{named.ShopId}' AND kind='CoverageSlotConsent'"))!;

        // Commit order 2: Term 1 restoration commits FIRST.
        var restoredQuote = await AcceptAsync(p, await RenewalQuoteAsync(p, term1, "Month"));
        var paid = await PayAsync(p, restoredQuote);
        P0302Fixture.AssertStatus(paid, "Verified", "Fulfilled", "Applying");
        var restoredTerm = paid.GetProperty("termId").GetGuid();

        // Now deliver the pending slot consent for Term 2 -> must be refused (ShopCovered), and replay is idempotent.
        await fixture.Checkout.Delivery.DrainAsync(false);
        Assert.Equal("Refused", await fixture.ScalarAsync($"SELECT state FROM coverage_slot_assignments WHERE assignment_id='{assignmentId}'"));
        Assert.Equal("ShopCovered", await fixture.ScalarAsync($"SELECT reason FROM coverage_slot_assignments WHERE assignment_id='{assignmentId}'"));
        using var redelivery = await DeliverConsentAsync(fixture.Checkout.Delivery, bytes);
        Assert.Equal(HttpStatusCode.NoContent, redelivery.StatusCode);

        Assert.Equal(0L, await fixture.CountAsync("payment_fulfilment_shops", $"shop_id='{named.ShopId}' AND term_id='{term2}'"));
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilment_shops", $"shop_id='{named.ShopId}' AND term_id='{restoredTerm}'"));
        Assert.Equal(restoredTerm, (await ShopTermAsync(p, named)).GetProperty("termId").GetGuid());
    }
}
