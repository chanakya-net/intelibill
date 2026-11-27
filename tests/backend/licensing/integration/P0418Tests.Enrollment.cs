using System.Net;
using System.Net.Http.Json;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0418Tests
{
    [Fact]
    public async Task Delivery_keeps_production_observe_only_and_runtime_cannot_activate()
    {
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() });
        const string activeCount = "SELECT count(*) FROM shop_license_states WHERE enforcement_activated_at IS NOT NULL";
        var priorActivations = (long)(await fixture.MerchantScalarAsync(activeCount))!;
        var (shop, account) = await PrepareShopAsync(fixture, enroll: false);
        using var client = shop.Client;
        var (service, _) = await AddServiceAsync(shop);
        using var admin = await P0403Tests.SignInSecurityAsync(fixture);
        var path = $"/api/admin/accounts/{account:D}/restrictions";
        var id = await P0404Tests.SuspendAsync(admin, path, "Shop", shop.ShopId, "Trading");
        await DrainAsync(fixture);
        await P0403Tests.AssertPropagationAsync(admin, path, id, "Acknowledged");

        using var observe = await client.GetAsync(AuthorityPath(shop.ShopId));
        var decision = await ReadAsync(observe, HttpStatusCode.Forbidden);
        Assert.Equal("Licence.TradingRestricted", decision.GetProperty("code").GetString());
        Assert.False(decision.GetProperty("enforced").GetBoolean());
        using var allowed = await client.PostAsJsonAsync("/api/sales", SaleRequest(service, "p0418-observe"));
        Assert.True(allowed.StatusCode == HttpStatusCode.Created, await allowed.Content.ReadAsStringAsync());

        var runtime = database.Login(LicensingPostgreSqlFixture.MerchantDatabase, LicensingPostgreSqlFixture.MerchantRole);
        var denied = await Assert.ThrowsAsync<PostgresException>(() => LicensingPostgreSqlFixture.ExecuteAsync(runtime, $"""
            SELECT set_config('app.active_shop_id', '{shop.ShopId:D}', false);
            UPDATE public.shop_license_states SET enforcement_activated_at = now() WHERE shop_id = '{shop.ShopId:D}';
            """));
        Assert.Equal("55000", denied.SqlState);
        await ReleaseAsync(admin, path, id);
        await DrainAsync(fixture);
        Assert.Equal(priorActivations, await fixture.MerchantScalarAsync(activeCount));

        await P0403Tests.EnrollAsync(fixture, shop);
        var clear = await Assert.ThrowsAsync<PostgresException>(() => fixture.MerchantScalarAsync($"""
            UPDATE public.shop_license_states SET enforcement_activated_at = NULL WHERE shop_id = '{shop.ShopId:D}' RETURNING shop_id
            """));
        Assert.Equal("55000", clear.SqlState);
        Assert.Equal(priorActivations + 1, await fixture.MerchantScalarAsync(activeCount));
    }
}
