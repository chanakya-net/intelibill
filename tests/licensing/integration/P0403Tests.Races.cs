using System.Net;
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

    private static Task<HttpResponseMessage> ReadAuthorityAsync(Merchant shop) =>
        shop.Client.GetAsync(new Uri($"/api/licensing/shops/{shop.ShopId:D}/trading-authority", UriKind.Relative));

    private static async Task WaitForBlockedAsync(P0213Fixture fixture, int holder, long expected)
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
