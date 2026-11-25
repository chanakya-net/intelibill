using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0604Tests
{
    [Fact]
    public async Task RenewalVerifiedDuringGraceKeepsTheEarlierGraceReadForAssignedShopAndSibling()
    {
        await using var fixture = await StartAsync(database, January31);
        var (p, term1) = await PrepareAsync(fixture);
        var (sibling, joined) = (p.Shops[0], p.Shops[2]);
        await AcceptSlotAsync(p, term1, joined);
        await fixture.Checkout.Delivery.DrainAsync(false);

        // During grace, before any renewal is verified, both shops hold grace coverage from their own starts.
        var inGrace = February29.AddDays(3);
        SyncAt(fixture, inGrace);
        AssertGrace(await ShopTermAsync(p, joined), term1, February14);
        AssertGrace(await ShopTermAsync(p, sibling), term1, January31);

        // The renewal is verified two days later and preserves the original paid-through anchor.
        SyncAt(fixture, February29.AddDays(5));
        var (quote, paid) = await RenewAsync(p, term1, "Month");
        Assert.Equal(February29, quote.PreviewStartsAt);
        var term2 = paid.GetProperty("termId").GetGuid();
        foreach (var (shop, starts) in new[] { (joined, February14), (sibling, January31) })
        {
            var renewed = await ShopTermAsync(p, shop);
            Assert.Equal((term2, "Active"), (renewed.GetProperty("termId").GetGuid(), renewed.GetProperty("state").GetString()));
            Assert.Equal(starts, renewed.GetProperty("startsAt").GetDateTimeOffset());
        }

        // Re-evaluating the earlier instant still finds the grace coverage that was effective then.
        SyncAt(fixture, inGrace);
        AssertGrace(await ShopTermAsync(p, joined), term1, February14);
        AssertGrace(await ShopTermAsync(p, sibling), term1, January31);
    }

    private static void AssertGrace(JsonElement term, Guid termId, DateTimeOffset startsAt)
    {
        Assert.Equal(termId, term.GetProperty("termId").GetGuid());
        Assert.Equal("Grace", term.GetProperty("state").GetString());
        Assert.Equal(startsAt, term.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal(February29, term.GetProperty("paidThrough").GetDateTimeOffset());
    }
}
