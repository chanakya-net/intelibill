using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0605Tests
{
    [Fact]
    public async Task VerifiedEarlyRenewalMovesOnlyTheSuccessorLinkAtTheExactBoundary()
    {
        await using var f = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(f);
        var original = await OriginalHistoryDigestAsync(f, p, term);
        var (id, _) = await ConsentAsync(f, p, term);
        var quote = await RenewalQuoteAsync(p, term, "Month");
        Assert.Equal(CheckoutOrdersV1.ShopHash([p.Shops[0].ShopId, p.Shops[1].ShopId, p.Shops[3].ShopId]), quote.ShopHash);
        var status = await PayAsync(p, await AcceptAsync(p, quote));
        var successor = status.GetProperty("termId").GetGuid();
        Assert.Equal(original, await OriginalHistoryDigestAsync(f, p, term));
        Assert.Equal(1L, await f.CountAsync("payment_fulfilment_shops", $"term_id='{term}' AND shop_id='{p.Shops[2].ShopId}'"));
        Assert.Equal(1L, await f.CountAsync("payment_fulfilment_shops", $"term_id='{successor}' AND shop_id='{p.Shops[3].ShopId}'"));
        Assert.Equal(0L, await f.CountAsync("payment_fulfilment_shops", $"term_id='{successor}' AND shop_id='{p.Shops[2].ShopId}'"));
        await f.Checkout.Delivery.DrainAsync(true);
        await f.Checkout.Delivery.DrainAsync(false);
        var pending = await P0302Fixture.StatusAsync(p.Payer.Client, quote);
        Assert.Equal("Applying", pending.GetProperty("access").GetString());
        await VerifyTradingAsync(f, p, February29.AddTicks(-10), "Allowed", "Licence.PendingActivation");
        Assert.Equal("Active", (await ShopTermAsync(p, p.Shops[2])).GetProperty("state").GetString());
        Assert.Equal(0L, await f.Checkout.Delivery.MerchantScalarAsync($"SELECT count(*) FROM coverage_replacement_activations WHERE replacement_id='{id}'"));
        await VerifyTradingAsync(f, p, February29, "Licence.Expired", "Allowed", deliverReceipts: true);
        Assert.Equal(1L, await f.Checkout.Delivery.MerchantScalarAsync($"SELECT count(*) FROM coverage_replacement_activations WHERE replacement_id='{id}'"));
        var confirmed = await P0302Fixture.StatusAsync(p.Payer.Client, quote);
        Assert.Equal("Applied", confirmed.GetProperty("access").GetString());
        var preview = await p.Payer.Client.GetFromJsonAsync<JsonElement>(PathFor(p, term));
        Assert.Equal("Acknowledged", Assert.Single(preview.GetProperty("replacements").EnumerateArray()).GetProperty("activation").GetString());
    }

    internal static async Task VerifyTradingAsync(P0308Fixture f, P0308Fixture.Package p, DateTimeOffset at,
        string outgoing, string incoming, bool deliverReceipts = false)
    {
        // Browser credentials stay on the session clock while the two service pumps sign and evaluate
        // these boundary snapshots at the same controlled instant as the merchant trading fence.
        f.At(at);
        var sessionClock = f.Hosts.Fixture.Clock;
        var original = sessionClock.Now;
        try
        {
            sessionClock.Now = at;
            f.Checkout.Delivery.Transport.LeaseClock.Now = at.AddSeconds(1);
            await f.Checkout.Delivery.ScanAsync(true);
            await f.Checkout.Delivery.DrainAsync(true);
            if (deliverReceipts) await f.Checkout.Delivery.DrainAsync(false);
            Assert.Equal(outgoing, await P0604Tests.FencedTradingAsync(f, p.Shops[2].ShopId));
            Assert.Equal(incoming, await P0604Tests.FencedTradingAsync(f, p.Shops[3].ShopId));
        }
        finally { sessionClock.Now = original; }
    }
}
