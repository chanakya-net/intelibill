using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0418Tests
{
    [Theory]
    [InlineData("Shop", true)]
    [InlineData("Shop", false)]
    [InlineData("Account", true)]
    [InlineData("Account", false)]
    public async Task Provider_suspension_and_sale_serialize_on_the_merchant_fence(string scope, bool deliveryFirst)
    {
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() });
        var (shop, account) = await PrepareShopAsync(fixture);
        using var client = shop.Client;
        var (service, _) = await AddServiceAsync(shop);
        using var admin = await P0403Tests.SignInSecurityAsync(fixture);
        var path = $"/api/admin/accounts/{account:D}/restrictions";
        var id = await P0404Tests.SuspendAsync(admin, path, scope, scope == "Shop" ? shop.ShopId : null, "Trading");
        await P0403Tests.AssertPropagationAsync(admin, path, id, "Pending");

        await using var holder = new NpgsqlConnection(fixture.MerchantDb.ConnectionString);
        await holder.OpenAsync();
        await using var fence = await holder.BeginTransactionAsync();
        await using (var acquire = new NpgsqlCommand("SELECT public.take_merchant_licence_fence($1, $2, false)", holder, fence))
        {
            acquire.Parameters.AddWithValue(account);
            acquire.Parameters.AddWithValue(shop.ShopId);
            await acquire.ExecuteNonQueryAsync();
        }

        const string key = "p0418-race";
        var delivery = deliveryFirst ? fixture.DeliverAsync(source: true) : null;
        var sale = deliveryFirst ? null : client.PostAsJsonAsync("/api/sales", SaleRequest(service, key));
        await P0403Tests.WaitForBlockedAsync(fixture, holder.ProcessID, 1);
        delivery ??= fixture.DeliverAsync(source: true);
        sale ??= client.PostAsJsonAsync("/api/sales", SaleRequest(service, key));
        await P0403Tests.WaitForBlockedAsync(fixture, holder.ProcessID, 2);
        Assert.Equal(0L, await CountSalesAsync(fixture, shop.ShopId));
        await fence.CommitAsync();

        Assert.True(await delivery);
        using var response = await sale;
        var body = await ReadAsync(response, deliveryFirst ? HttpStatusCode.Forbidden : HttpStatusCode.Created);
        Assert.Equal(deliveryFirst ? 0L : 1L, await CountSalesAsync(fixture, shop.ShopId));
        if (deliveryFirst)
            Assert.Equal("Licence.TradingRestricted", body.GetProperty("code").GetString());
        else
            await AssertReplayAsync(client, service, key, body);

        using var rejected = await client.PostAsJsonAsync("/api/sales", SaleRequest(service, "p0418-fresh"));
        Assert.Equal("Licence.TradingRestricted", await P0206Fixture.CodeAsync(rejected));
        await fixture.DrainAsync(source: false);
        await P0403Tests.AssertPropagationAsync(admin, path, id, "Acknowledged");
    }

    private static async Task AssertReplayAsync(HttpClient client, Guid service, string key, JsonElement original)
    {
        using var replay = await client.PostAsJsonAsync("/api/sales", SaleRequest(service, key));
        var body = await ReadAsync(replay, HttpStatusCode.Created);
        Assert.Equal(original.GetProperty("saleId").GetGuid(), body.GetProperty("saleId").GetGuid());
        Assert.Equal(original.GetProperty("invoiceNumber").GetString(), body.GetProperty("invoiceNumber").GetString());
    }

    private static Task<object?> CountSalesAsync(P0213Fixture fixture, Guid shop) =>
        fixture.MerchantScalarAsync($"SELECT count(*) FROM sales WHERE shop_id = '{shop:D}'");
}
