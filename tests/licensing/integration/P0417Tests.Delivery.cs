using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0417Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MerchantAppliesOnlyAnExplicitReopeningAndAcknowledgesIt(bool reordered)
    {
        var clock = new P0201Clock { Now = P0202Fixture.WholeSecondNow() };
        await using var fixture = await P0213Fixture.StartAsync(database, businessClock: clock);
        var (owner, account, route) = await CloseAsync(fixture, clock.Now, till: true);
        var closed = await fixture.LatestSnapshotAsync(owner.ShopId);
        await fixture.DrainAsync(source: false);
        await fixture.MerchantScalarAsync($"""
            UPDATE shop_license_states SET enforcement_activated_at = now() WHERE shop_id = '{owner.ShopId:D}'
            RETURNING shop_id
            """);
        if (!reordered)
        {
            await fixture.DrainAsync(source: true);
            await AssertUnprovedReopeningRejectedAsync(fixture, closed);
            using var denied = await P0403Tests.WriteTillAsync(owner, "closed-till", "Still closed");
            Assert.Equal("Licence.TradingRestricted", await P0206Fixture.CodeAsync(denied));
        }
        using var admin = await SignInAsync(fixture);
        var impact = await PreviewAsync(admin, route);
        using var reopened = await P0212Fixture.PostAsync(admin, route, Command(impact, owner));
        Assert.True(reopened.StatusCode == HttpStatusCode.Created, await reopened.Content.ReadAsStringAsync());
        await AssertPropagationAsync(admin, account, "Pending");
        await AssertMerchantStatusAsync(owner, false);

        if (reordered)
        {
            var (http, receipt) = await fixture.SendAsync(await fixture.LatestSnapshotAsync(owner.ShopId));
            http.Dispose();
            Assert.Equal("Applied", receipt?.Disposition);
            var (oldHttp, oldReceipt) = await fixture.SendAsync(closed);
            oldHttp.Dispose();
            Assert.Equal("Superseded", oldReceipt?.Disposition);
        }
        await fixture.DrainAsync(source: true);
        var snapshot = await fixture.LatestSnapshotAsync(owner.ShopId);
        var projection = await fixture.MerchantScalarAsync($"""
            SELECT concat_ws('|', state, revision, reopened_revision) FROM shop_license_states WHERE shop_id = '{owner.ShopId:D}'
            """);
        var delivery = await fixture.SourceScalarAsync($"""
            SELECT concat_ws('|', status, last_failure) FROM entitlement_outbox WHERE event_id = '{snapshot.EventId:D}'
            """);
        Assert.True(Equals($"Active|{snapshot.Shop!.Revision}|{snapshot.Shop.ReopenedRevision}", projection),
            $"Expected current reopening projection; observed {projection}; provider delivery {delivery}.");
        await AssertMerchantStatusAsync(owner, true);
        await AssertPropagationAsync(admin, account, "Pending");
        await fixture.DrainAsync(source: false);
        await AssertPropagationAsync(admin, account, "Acknowledged");
        using var till = await P0403Tests.WriteTillAsync(owner, "reopened-till", "Reopened till");
        Assert.True(till.IsSuccessStatusCode, await till.Content.ReadAsStringAsync());
    }

    private static async Task AssertUnprovedReopeningRejectedAsync(P0213Fixture fixture,
        Intelibill.Shared.Contracts.V1.EntitlementSnapshotV1 closed)
    {
        foreach (var cutoff in new long?[] { null, closed.Shop!.Revision - 1 })
        {
            var forged = closed with { EventId = Guid.NewGuid(), Shop = closed.Shop with
            {
                Revision = closed.Shop.Revision + 1, State = "Active", ReopenedRevision = cutoff,
            } };
            var (http, receipt) = await fixture.SendAsync(forged);
            http.Dispose();
            Assert.Equal("ConflictingShopScope", receipt?.Disposition);
        }
    }

    private static async Task AssertMerchantStatusAsync(Merchant owner, bool applied)
    {
        var status = await owner.Client.GetFromJsonAsync<JsonElement>($"/api/licensing/shops/{owner.ShopId:D}/status");
        Assert.Equal(JsonValueKind.String, status.GetProperty("reopenedAt").ValueKind);
        Assert.Equal(applied, status.GetProperty("reopeningEnforced").GetBoolean());
        Assert.False(status.TryGetProperty("reopeningRequiredRevision", out _));
    }

    private static async Task AssertPropagationAsync(P0110CookieBrowser admin, Guid account, string expected)
    {
        using var response = await admin.SendAsync(HttpMethod.Get, $"/api/admin/accounts/{account:D}/closures");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var closure = Assert.Single((await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
        var shop = Assert.Single(closure.GetProperty("shops").EnumerateArray());
        Assert.Equal(expected, shop.GetProperty("reopening").GetProperty("propagation").GetString());
    }
}
