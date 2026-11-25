using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0605Tests
{
    internal static async Task<(Guid Id, string Path)> ConsentAsync(P0308Fixture f, Package p, Guid term, int capacity = 3)
    {
        var (id, path) = await QueueConsentAsync(p, term, capacity);
        await f.Checkout.Delivery.DrainAsync(false);
        Assert.Equal(1L, await f.CountAsync("coverage_replacements", $"replacement_id='{id}' AND state='Scheduled'"));
        return (id, path);
    }

    private static async Task<(Guid Id, string Path)> QueueConsentAsync(Package p, Guid term, int capacity = 3)
    {
        using var request = await p.Payer.Client.PostAsJsonAsync(PathFor(p, term), Request(p, Guid.NewGuid(), capacity: capacity));
        request.EnsureSuccessStatusCode();
        var id = (await request.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replacementId").GetGuid();
        var shop = p.Shops[3];
        var path = $"/api/billing/accounts/{p.Account}/coverage-replacements/{shop.ShopId}/{id}/consent";
        using var denied = await P0604Tests.Decide(p.Payer.Client, path, "grant");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        foreach (var decision in new[] { "grant", "accept" })
        {
            using var result = await P0604Tests.Decide(shop.Client, path, decision);
            Assert.True(result.IsSuccessStatusCode, await result.Content.ReadAsStringAsync());
        }
        return (id, path);
    }

    [Fact]
    public async Task IncomingOwnerCanRevokeAcceptedConsentBeforeRenewal()
    {
        await using var f = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(f);
        var (id, path) = await ConsentAsync(f, p, term);
        using var revoke = await P0604Tests.Decide(p.Shops[3].Client, path, "revoke");
        revoke.EnsureSuccessStatusCode();
        await f.Checkout.Delivery.DrainAsync(false);
        Assert.Equal(1L, await f.CountAsync("coverage_replacements", $"replacement_id='{id}' AND reason='ConsentRevoked'"));
        Assert.Equal(0L, await f.CountAsync("payment_fulfilment_shops", $"shop_id='{p.Shops[3].ShopId}'"));
    }
}
