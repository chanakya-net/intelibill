using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0605Tests
{
    internal static object Request(Package p, Guid key, Guid? incoming = null, bool acknowledge = true, int capacity = 3) => new
    {
        operationKey = key, outgoingShopId = p.Shops[2].ShopId, incomingShopId = incoming ?? p.Shops[3].ShopId,
        observedEffectiveAt = February29, observedCapacity = capacity, observedMemberAllowance = 5,
        observedPermitDeadline = (DateTimeOffset?)null, acknowledgedRenewal = acknowledge,
        acknowledgedPermitWait = true, acknowledgedShopSelection = true,
    };

    [Fact]
    public async Task SchedulingIsIdempotentAndLeavesEveryCurrentLinkIntact()
    {
        await using var fixture = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(fixture);
        var path = PathFor(p, term);
        var key = Guid.NewGuid();
        using var result = await p.Payer.Client.PostAsJsonAsync(path, Request(p, key));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var row = await result.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("AwaitingConsent", row.GetProperty("state").GetString());
        Assert.Equal(February29, row.GetProperty("effectiveAt").GetDateTimeOffset());
        using var replay = await p.Payer.Client.PostAsJsonAsync(path, Request(p, key));
        var again = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(row.GetProperty("replacementId").GetGuid(), again.GetProperty("replacementId").GetGuid());
        using var mismatch = await p.Payer.Client.PostAsJsonAsync(path, Request(p, key, p.Shops[0].ShopId));
        Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        Assert.Equal(3L, await fixture.CountAsync("payment_fulfilment_shops", $"term_id='{term}'"));
        Assert.Equal(0L, await fixture.CountAsync("payment_fulfilment_shops", $"shop_id='{p.Shops[3].ShopId}'"));
    }

    [Fact]
    public async Task ConcurrentSchedulesCannotReserveTheSameShopAndAnActiveShopCannotBeSelected()
    {
        await using var fixture = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(fixture);
        var path = PathFor(p, term);
        using var missing = await p.Payer.Client.PostAsJsonAsync(path, Request(p, Guid.NewGuid(), acknowledge: false));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using var covered = await p.Payer.Client.PostAsJsonAsync(path, Request(p, Guid.NewGuid(), p.Shops[1].ShopId));
        Assert.Equal(HttpStatusCode.Conflict, covered.StatusCode);
        var race = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            p.Payer.Client.PostAsJsonAsync(path, Request(p, Guid.NewGuid()))));
        Assert.Single(race, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(race, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1L, await fixture.CountAsync("coverage_replacements"));
    }
}
