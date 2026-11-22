using System.Net;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0418Tests
{
    [Theory]
    [InlineData("Trading")]
    [InlineData("Security")]
    public async Task History_and_identity_obey_the_suspension_scope(string mode)
    {
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() });
        var (shop, account) = await PrepareShopAsync(fixture);
        var (other, _) = await PrepareShopAsync(fixture);
        using var client = shop.Client;
        using var unrelated = other.Client;
        using var admin = await P0403Tests.SignInSecurityAsync(fixture);
        var path = $"/api/admin/accounts/{account:D}/restrictions";
        await P0404Tests.SuspendAsync(admin, path, "Shop", shop.ShopId, mode);
        await DrainAsync(fixture);

        var expected = mode == "Security" ? HttpStatusCode.Forbidden : HttpStatusCode.OK;
        string[] history = ["/api/sales", "/api/customers", "/api/suppliers", "/api/expenses",
            "/api/purchase-orders", "/api/dashboard", "/api/inventory/batches", "/api/tills", "/api/services"];
        foreach (var route in history)
        {
            using var response = await client.GetAsync(route);
            var body = await ReadAsync(response, expected);
            Assert.True(response.Headers.CacheControl?.NoStore, route);
            if (expected == HttpStatusCode.Forbidden)
                Assert.Equal("Licence.SecuritySuspended", body.GetProperty("code").GetString());
            using var sibling = await unrelated.GetAsync(route);
            Assert.True(sibling.StatusCode == HttpStatusCode.OK, $"{route}: {await sibling.Content.ReadAsStringAsync()}");
        }

        using var sessions = await client.GetAsync("/api/users/me/sessions");
        Assert.Equal(HttpStatusCode.OK, sessions.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(await P0201Tests.LoginAsync(fixture.MerchantApi, shop.Email)));
    }
}
