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
        f.At(February29.AddTicks(-10));
        Assert.Equal("Active", (await ShopTermAsync(p, p.Shops[2])).GetProperty("state").GetString());
        Assert.Equal(0L, await f.Checkout.Delivery.MerchantScalarAsync($"SELECT count(*) FROM coverage_replacement_activations WHERE replacement_id='{id}'"));
        f.At(February29);
        await f.Checkout.Delivery.ScanAsync(true);
        await f.Checkout.Delivery.DrainAsync(true);
        await f.Checkout.Delivery.DrainAsync(false);
        Assert.Equal(1L, await f.Checkout.Delivery.MerchantScalarAsync($"SELECT count(*) FROM coverage_replacement_activations WHERE replacement_id='{id}'"));
        var confirmed = await P0302Fixture.StatusAsync(p.Payer.Client, quote);
        Assert.Equal("Applied", confirmed.GetProperty("access").GetString());
        var preview = await p.Payer.Client.GetFromJsonAsync<JsonElement>(PathFor(p, term));
        Assert.Equal("Acknowledged", Assert.Single(preview.GetProperty("replacements").EnumerateArray()).GetProperty("activation").GetString());
    }
}
