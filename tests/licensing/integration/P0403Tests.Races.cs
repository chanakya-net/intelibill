using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0403Tests
{
    [Theory]
    [InlineData("Account")]
    [InlineData("Shop")]
    public async Task FencedAuthorityQueuedBehindDeliverySeesTheSuspension(string scope)
    {
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() });
        var shop = await CreateTradingShopAsync(fixture.Hosts);
        var account = await LinkAsync(fixture, shop);
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
        await EnrollAsync(fixture, shop);

        using var security = await SignInSecurityAsync(fixture);
        var effectiveAt = fixture.Hosts.Fixture.Clock.Now.AddSeconds(10);
        await ApplySuspensionAsync(security, account, scope, scope == "Shop" ? shop.ShopId : null, "Trading", effectiveAt);
        fixture.Hosts.Fixture.Clock.Now = effectiveAt;
        var snapshot = await fixture.LatestSnapshotAsync(shop.ShopId);
        using (var unacknowledged = await ReadAuthorityAsync(shop))
            Assert.True(unacknowledged.StatusCode == HttpStatusCode.OK, await unacknowledged.Content.ReadAsStringAsync());

        await using var holder = new NpgsqlConnection(fixture.MerchantDb.ConnectionString);
        await holder.OpenAsync();
        await using var fence = await holder.BeginTransactionAsync();
        await using (var acquire = new NpgsqlCommand("SELECT public.take_merchant_licence_fence($1, $2, false)", holder, fence))
        {
            acquire.Parameters.AddWithValue(account);
            acquire.Parameters.AddWithValue(shop.ShopId);
            await acquire.ExecuteNonQueryAsync();
        }

        var delivery = fixture.SendAsync(snapshot);
        await WaitForBlockedAsync(fixture, holder.ProcessID, 1);
        var queued = ReadAuthorityAsync(shop);
        await WaitForBlockedAsync(fixture, holder.ProcessID, 2);
        await fence.CommitAsync();

        var (http, ack) = await delivery;
        http.Dispose();
        Assert.Equal("Applied", ack?.Outcome);
        using var refused = await queued;
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("Licence.TradingRestricted", await P0206Fixture.CodeAsync(refused));
    }

    [Theory]
    [InlineData("Account", true, true)]
    [InlineData("Shop", true, true)]
    [InlineData("Account", false, true)]
    [InlineData("Account", true, false)]
    public async Task KeyedTillWriteSerializesWithRestrictionDelivery(string scope, bool deliveryFirst, bool enrolled)
    {
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() });
        var shop = await CreateTradingShopAsync(fixture.Hosts);
        var account = await LinkAsync(fixture, shop);
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
        await OpenTillAsync(fixture, shop);
        if (enrolled) await EnrollAsync(fixture, shop);
        using var security = await SignInSecurityAsync(fixture);
        await ApplySuspensionAsync(security, account, scope, scope == "Shop" ? shop.ShopId : null, "Trading");
        var snapshot = await fixture.LatestSnapshotAsync(shop.ShopId);
        const string key = "queued-till";
        const string name = "Queued till";

        await using var holder = new NpgsqlConnection(fixture.MerchantDb.ConnectionString);
        await holder.OpenAsync();
        await using var fence = await holder.BeginTransactionAsync();
        await using (var acquire = new NpgsqlCommand("SELECT public.take_merchant_licence_fence($1, $2, false)", holder, fence))
        {
            acquire.Parameters.AddWithValue(account);
            acquire.Parameters.AddWithValue(shop.ShopId);
            await acquire.ExecuteNonQueryAsync();
        }

        var delivery = deliveryFirst ? fixture.SendAsync(snapshot) : null;
        var write = deliveryFirst ? null : WriteTillAsync(shop, key, name);
        await WaitForBlockedAsync(fixture, holder.ProcessID, 1);
        delivery ??= fixture.SendAsync(snapshot);
        write ??= WriteTillAsync(shop, key, name);
        await WaitForBlockedAsync(fixture, holder.ProcessID, 2);
        await fence.CommitAsync();

        var (http, ack) = await delivery;
        http.Dispose();
        Assert.Equal("Applied", ack?.Outcome);
        using var outcome = await write;
        var refused = enrolled && deliveryFirst;
        Assert.Equal(refused ? HttpStatusCode.Forbidden : HttpStatusCode.OK, outcome.StatusCode);
        if (refused) Assert.Equal("Licence.TradingRestricted", await P0206Fixture.CodeAsync(outcome));
        Assert.Equal(refused ? 0L : 1L, await fixture.MerchantScalarAsync($"""
            SELECT count(*) FROM tills WHERE shop_id = '{shop.ShopId:D}' AND name = 'Queued till'
            """));
        Assert.Equal(refused ? 0L : 1L, await fixture.MerchantScalarAsync($"""
            SELECT count(*) FROM operation_receipts WHERE shop_id = '{shop.ShopId:D}' AND operation_key = 'queued-till'
            """));
    }

    [Theory]
    [InlineData("Trading")]
    [InlineData("Security")]
    public async Task TillReplayRequiresRecoveryButNotTradingPermission(string mode)
    {
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() });
        var shop = await CreateTradingShopAsync(fixture.Hosts);
        var account = await LinkAsync(fixture, shop);
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
        await OpenTillAsync(fixture, shop);
        await EnrollAsync(fixture, shop);
        using var first = await WriteTillAsync(shop, "committed", "Committed till");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var committed = await first.Content.ReadFromJsonAsync<JsonElement>();
        using var security = await SignInSecurityAsync(fixture);
        await ApplySuspensionAsync(security, account, "Account", null, mode);
        await fixture.DrainAsync(source: true);

        using var replay = await WriteTillAsync(shop, "committed", "Committed till");
        using var changed = await WriteTillAsync(shop, "committed", "Changed till");
        using var fresh = await WriteTillAsync(shop, "fresh", "Fresh till");
        if (mode == "Security")
        {
            foreach (var response in new[] { replay, changed, fresh })
            {
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Equal("Licence.SecuritySuspended", await P0206Fixture.CodeAsync(response));
            }
        }
        else
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(committed.GetProperty("id").GetGuid(),
                (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
            Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
            Assert.Contains("Operation.PayloadMismatch", await changed.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Forbidden, fresh.StatusCode);
            Assert.Equal("Licence.TradingRestricted", await P0206Fixture.CodeAsync(fresh));
        }

        Assert.Equal(1L, await fixture.MerchantScalarAsync($"""
            SELECT count(*) FROM operation_receipts WHERE shop_id = '{shop.ShopId:D}'
                AND operation_key IN ('committed', 'fresh')
            """));
    }

    internal static Task<HttpResponseMessage> WriteTillAsync(Merchant shop, string key, string name) =>
        shop.Client.PostAsJsonAsync("/api/tills", new { name, operationKey = key });

    private static Task<HttpResponseMessage> ReadAuthorityAsync(Merchant shop) =>
        shop.Client.GetAsync(new Uri($"/api/licensing/shops/{shop.ShopId:D}/trading-authority", UriKind.Relative));

    internal static async Task WaitForBlockedAsync(P0213Fixture fixture, int holder, long expected)
    {
        long blocked = 0;
        for (var attempt = 0; attempt < 80 && blocked < expected; attempt++)
        {
            blocked = (long)(await fixture.MerchantScalarAsync($"""
                SELECT count(*) FROM pg_stat_activity WHERE {holder} = ANY(pg_blocking_pids(pid))
                """))!;
            if (blocked < expected) await Task.Delay(TimeSpan.FromMilliseconds(25));
        }
        Assert.Equal(expected, blocked);
    }
}
