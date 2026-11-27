using System.Net;
using Intelibill.Shared.Contracts.V1;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0404Tests
{
    private const string TillKey = "release-queued-till";
    private const string TillName = "Release queued till";

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task KeyedTillWriteObservesReleaseAtTheExistingFence(bool deliveryFirst, bool shopStillRestricted)
    {
        await using var fixture = await StartAsync();
        var shop = await CreateTradingShopAsync(fixture.Hosts);
        var account = await LinkAsync(fixture, shop);
        await fixture.DrainAsync(source: true);
        await P0403Tests.OpenTillAsync(fixture, shop);
        await EnrollAsync(fixture, shop);
        using var admin = await SignInSecurityAsync(fixture);
        var path = $"/api/admin/accounts/{account:D}/restrictions";
        var id = await SuspendAsync(admin, path, "Account", null, "Trading");
        if (shopStillRestricted) await SuspendAsync(admin, path, "Shop", shop.ShopId, "Trading");
        await fixture.DrainAsync(source: true);
        await SubmitReleaseAsync(admin, $"{path}/{id:D}/release");
        var release = await fixture.LatestSnapshotAsync(shop.ShopId);

        using var outcome = await RaceTillWriteAsync(fixture, shop, release, deliveryFirst);
        var allowed = deliveryFirst && !shopStillRestricted;
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, outcome.StatusCode);
        if (!allowed) Assert.Equal("Licence.TradingRestricted", await P0206Fixture.CodeAsync(outcome));
        Assert.Equal(allowed ? 1L : 0L, await fixture.MerchantScalarAsync($"""
            SELECT count(*) FROM tills WHERE shop_id = '{shop.ShopId:D}' AND name = '{TillName}'
            """));
        Assert.Equal(allowed ? 1L : 0L, await fixture.MerchantScalarAsync($"""
            SELECT count(*) FROM operation_receipts WHERE shop_id = '{shop.ShopId:D}' AND operation_key = '{TillKey}'
            """));
    }

    private static async Task<HttpResponseMessage> RaceTillWriteAsync(P0213Fixture fixture, Merchant shop,
        EntitlementSnapshotV1 release, bool deliveryFirst)
    {
        await using var holder = new NpgsqlConnection(fixture.MerchantDb.ConnectionString);
        await holder.OpenAsync();
        await using var fence = await holder.BeginTransactionAsync();
        await using var acquire = new NpgsqlCommand("SELECT public.take_merchant_licence_fence($1, $2, false)", holder, fence);
        acquire.Parameters.AddWithValue(release.AccountId);
        acquire.Parameters.AddWithValue(shop.ShopId);
        await acquire.ExecuteNonQueryAsync();

        var delivery = deliveryFirst ? fixture.SendAsync(release) : null;
        var write = deliveryFirst ? null : P0403Tests.WriteTillAsync(shop, TillKey, TillName);
        await P0403Tests.WaitForBlockedAsync(fixture, holder.ProcessID, 1);
        delivery ??= fixture.SendAsync(release);
        write ??= P0403Tests.WriteTillAsync(shop, TillKey, TillName);
        await P0403Tests.WaitForBlockedAsync(fixture, holder.ProcessID, 2);
        await fence.CommitAsync();

        var (http, acknowledgement) = await delivery;
        using (http) Assert.Equal("Applied", acknowledgement?.Outcome);
        return await write;
    }
}
