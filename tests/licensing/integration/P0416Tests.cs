using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0416Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task OwnerPreviewReportsOnlyObservedWork()
    {
        var clock = new P0201Clock { Now = P0202Fixture.WholeSecondNow() };
        await using var fixture = await P0213Fixture.StartAsync(database, businessClock: clock);
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var other = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(owner.Client);
        await P0213Fixture.LinkAsync(account, owner, owner);
        await fixture.DrainAsync(source: true);
        await P0403Tests.OpenTillAsync(fixture, owner);
        await SeedObservedSubmissionsAsync(fixture, owner, clock.Now);

        var route = $"/api/licensing/shops/{owner.ShopId:D}/closure-preview";
        using var response = await owner.Client.GetAsync(route);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"Expected closure preview; got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        Assert.True(response.Headers.CacheControl?.NoStore);
        var review = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(owner.ShopId, review.GetProperty("shopId").GetGuid());
        Assert.Equal(1, review.GetProperty("openTills").GetInt32());
        Assert.True(review.GetProperty("disconnectedQueuesUnknown").GetBoolean());
        Assert.Equal(2, review.GetProperty("observedSubmissions").GetInt32());
        Assert.Equal(1, review.GetProperty("observedDevices").GetInt32());
        var device = Assert.Single(review.GetProperty("devices").EnumerateArray());
        Assert.Equal(clock.Now, device.GetProperty("lastContactAt").GetDateTimeOffset());
        Assert.Equal("Unavailable", device.GetProperty("permitEvidence").GetString());
        Assert.DoesNotContain("direct-device", review.GetRawText());
        Assert.DoesNotContain("disconnected-device", review.GetRawText());

        using var denied = await other.Client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task ShopClosureRequiresAcknowledgementAndIsIsolated()
    {
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() });
        var payer = await CreateTradingShopAsync(fixture.Hosts);
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(payer.Client);
        await P0213Fixture.LinkAsync(account, payer, payer);
        await P0213Fixture.LinkAsync(account, payer, owner);
        await fixture.DrainAsync(source: true);
        var sibling = await fixture.LatestSnapshotAsync(payer.ShopId);
        var route = $"/api/licensing/shops/{owner.ShopId:D}";
        var preview = await owner.Client.GetFromJsonAsync<JsonElement>(route + "/closure-preview");
        var command = new
        {
            operationKey = Guid.NewGuid(),
            expectedVersion = preview.GetProperty("closureVersion").GetInt64(),
            ownerVersion = preview.GetProperty("ownerVersion").GetInt64(),
            workDigest = preview.GetProperty("workDigest").GetString(),
            reviewedKnownWork = true, consideredDeviceQueues = true,
            financialTreatment = "NoFinancialChange",
        };
        using var denied = await payer.Client.PostAsJsonAsync(route + "/closure", command);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var refused = await owner.Client.PostAsJsonAsync(route + "/closure",
            command with { consideredDeviceQueues = false });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var responses = await Task.WhenAll(owner.Client.PostAsJsonAsync(route + "/closure", command),
            owner.Client.PostAsJsonAsync(route + "/closure", command));
        using var saved = responses[0];
        using var replay = responses[1];
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
        Assert.Equal("Closed", (await fixture.LatestSnapshotAsync(owner.ShopId)).Shop?.State);
        Assert.Equal(sibling, await fixture.LatestSnapshotAsync(payer.ShopId));
        Assert.Equal(1L, await fixture.Trial.CountAsync("operational_closures"));
        Assert.Equal(await saved.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
    }
}
