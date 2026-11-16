using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0209Tests
{
    [Fact]
    public async Task UnavailableVerificationAndExpiredStatusDoNotBlockObserveOnlyCommerce()
    {
        var transport = new SwitchableTransport();
        await using var host = await StartAsync(database, Utc("2027-03-07T04:30:00Z"), inner =>
        {
            transport.InnerHandler = inner;
            return transport;
        });
        var payer = await SignInAsync(host);
        var account = await CreateAccountAsync(payer.Client);
        var owner = await CreateTradingShopAsync(host);
        var staff = await SignInAsync(host, ShopRole.Staff, owner.ShopId);
        var term = await SeedTermAsync(host, account, new TermSeed(Jan31, Feb28, AnchorLocal: "2027-01-31 10:00:00"));
        await AffiliateAsync(host, account, payer.UserId, owner.ShopId, owner.UserId, term, Jan31, Feb28);

        // Exactly at the grace end the term is Expired: a successful evaluation, not an outage.
        var expired = await ReadAsync(await ShopStatusAsync(owner.Client, owner.ShopId));
        Assert.Equal("Expired", expired.GetProperty("state").GetString());
        Assert.True(expired.GetProperty("observeOnly").GetBoolean());
        await PostStockAsync(owner.Client, "Expired licence stock");

        // An existing merchant authorization still refuses what it refused before.
        using var refused = await staff.Client.PutAsJsonAsync("/api/expiry-policies", new
        {
            expectedCatalogVersion = 1, reason = "Staff attempt",
            policy = new { mode = "Block", expiryRequired = true, sellThroughExpiryDate = true, warningDays = 7, overrideAllowed = false },
        });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        // Licensing unreachable: verification is unavailable, never a guessed expiry, and trading continues.
        transport.Unreachable = true;
        await AssertUnavailableAsync(await ShopStatusAsync(owner.Client, owner.ShopId));
        await PostStockAsync(owner.Client, "Unreachable licence stock");
        transport.Unreachable = false;

        // Licensing storage down: the same explicit unavailable condition.
        var name = host.Fixture.Owner.Database;
        await LicensingPostgreSqlFixture.ExecuteAsync(database.Bootstrap(), $"""ALTER DATABASE "{name}" ALLOW_CONNECTIONS false""");
        await LicensingPostgreSqlFixture.ExecuteAsync(database.Bootstrap(),
            $"SELECT pg_terminate_backend(pid, 5000) FROM pg_stat_activity WHERE datname = '{name}'");
        try
        {
            await AssertUnavailableAsync(await ShopStatusAsync(owner.Client, owner.ShopId));
            await PostStockAsync(owner.Client, "Storage outage stock");
        }
        finally
        {
            await LicensingPostgreSqlFixture.ExecuteAsync(database.Bootstrap(), $"""ALTER DATABASE "{name}" ALLOW_CONNECTIONS true""");
        }

        var recovered = await ReadAsync(await ShopStatusAsync(owner.Client, owner.ShopId));
        Assert.Equal("Expired", recovered.GetProperty("state").GetString());
        Assert.Equal(3L, await LicensingPostgreSqlFixture.ReadScalarAsync(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase),
            $"SELECT count(*) FROM public.inventory_batches WHERE shop_id = '{owner.ShopId:D}'"));
    }

    private static async Task AssertUnavailableAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.ServiceUnavailable, $"{(int)response.StatusCode}: {body}");
            Assert.Equal("Billing.DelegationUnavailable", JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
            Assert.DoesNotContain("Expired", body);
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
        }
    }

    internal static async Task PostStockAsync(HttpClient client, string name)
    {
        using var inbound = await client.PostAsJsonAsync("/api/inventory/inbound", new
        {
            itemName = name, barcode = Guid.NewGuid().ToString("N"), uom = "pcs", batchNumber = "p0209", quantity = 5m,
            totalPurchaseCost = 50m, mrp = 12m, salesPrice = 12m, taxRatePercent = 0m, taxIncluded = false,
            purchaseTaxIncluded = false,
        });
        Assert.True(inbound.IsSuccessStatusCode, await inbound.Content.ReadAsStringAsync());
    }

    private sealed class SwitchableTransport : DelegatingHandler
    {
        internal volatile bool Unreachable;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Unreachable ? throw new HttpRequestException("Local fixture: licensing host unreachable.")
                : base.SendAsync(request, cancellationToken);
    }
}
