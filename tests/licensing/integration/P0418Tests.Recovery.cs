using System.Net;
using System.Net.Http.Json;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0418Tests
{
    [Fact]
    public async Task Committed_invoice_and_replay_authority_survive_restriction_transitions()
    {
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() });
        var (shop, account) = await PrepareShopAsync(fixture);
        using var client = shop.Client;
        var (service, _) = await AddServiceAsync(shop);
        using var admin = await P0403Tests.SignInSecurityAsync(fixture);
        const string key = "p0418-committed-invoice";
        using var first = await client.PostAsJsonAsync("/api/sales", SaleRequest(service, key));
        var original = await ReadAsync(first, HttpStatusCode.Created);
        var saleId = original.GetProperty("saleId").GetGuid();
        var facts = await InvoiceFactsAsync(fixture, saleId);
        Assert.False(string.IsNullOrWhiteSpace(facts));

        var history = $"/api/sales/{saleId:D}";
        var lookup = $"/api/sales/by-idempotency-key/{key}";
        var path = $"/api/admin/accounts/{account:D}/restrictions";
        var trading = await P0404Tests.SuspendAsync(admin, path, "Shop", shop.ShopId, "Trading");
        await DrainAsync(fixture);
        await AssertReplayAsync(client, service, key, original);
        using (var source = await client.GetAsync(history))
            Assert.Equal(HttpStatusCode.OK, source.StatusCode);

        using (var mismatch = await client.PostAsJsonAsync("/api/sales", SaleRequest(Guid.NewGuid(), key)))
        {
            Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
            var problem = await ReadAsync(mismatch, HttpStatusCode.Conflict);
            Assert.Equal("Sale.IdempotencyConflict",
                (problem.TryGetProperty("code", out var code) ? code : problem.GetProperty("title")).GetString());
        }

        var security = await P0404Tests.SuspendAsync(admin, path, "Account", null);
        await DrainAsync(fixture);
        using (var replay = await client.PostAsJsonAsync("/api/sales", SaleRequest(service, key)))
            Assert.Equal("Licence.SecuritySuspended", await P0206Fixture.CodeAsync(replay));
        foreach (var url in new[] { history, lookup })
        {
            using var denied = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal("Licence.SecuritySuspended", await P0206Fixture.CodeAsync(denied));
        }

        await ReleaseAsync(admin, path, security);
        await ReleaseAsync(admin, path, trading);
        await DrainAsync(fixture);
        await AssertReplayAsync(client, service, key, original);
        Assert.Equal(facts, await InvoiceFactsAsync(fixture, saleId));
        Assert.Equal(1L, await CountSalesAsync(fixture, shop.ShopId));
    }

    private static async Task<string?> InvoiceFactsAsync(P0213Fixture fixture, Guid id) =>
        (string?)await fixture.MerchantScalarAsync($"SELECT to_jsonb(s)::text FROM sales s WHERE id = '{id:D}'");
}
