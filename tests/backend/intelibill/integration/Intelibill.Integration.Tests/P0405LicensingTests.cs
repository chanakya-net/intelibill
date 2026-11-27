using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using Intelibill.Application.Common.Licensing;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Billing;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Licensing;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit.Abstractions;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed partial class P0405LicensingTests(PostgreSqlTestFixture fixture, ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;
    private readonly TestClock _clock = new(EntitlementSnapshotsV1.Instant(TimeProvider.System));
    private string Migrator => fixture.MigratorConnectionString;
    private static CancellationToken Ct => CancellationToken.None;

    [Theory]
    [InlineData("Goods")]
    [InlineData("Service")]
    [InlineData("Mixed")]
    public async Task Enforced_expiry_refuses_new_sales_without_side_effects(string cart)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var goods = await SeedGoodsAsync(client);
        var service = await SeedServiceAsync(client);
        var binding = await BindAsync(shop);
        await ApplyAsync(binding, _clock.Now.AddMinutes(1));
        await EnforceAsync(shop);
        _clock.Now = _clock.Now.AddMinutes(1);

        var items = cart switch
        {
            "Goods" => new[] { goods },
            "Service" => new[] { service },
            _ => new[] { goods, service },
        };
        var request = SaleRequest(items);
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/sales", request, Ct),
            HttpStatusCode.Forbidden, "Licence.Expired");
        await AssertNoPostingAsync(shop, goods.InventoryBatchId);

        await ApplyAsync(binding, _clock.Now.AddDays(1), revision: 2);
        await PostSaleAsync(client, request);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shop_without_enforcement_keeps_selling(bool hasProjection)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        if (hasProjection)
        {
            await ApplyAsync(await BindAsync(shop), _clock.Now.AddMinutes(1));
            _clock.Now = _clock.Now.AddMinutes(1);
        }
        await PostSaleAsync(client, SaleRequest(await SeedServiceAsync(client)));
    }

    private ApiWebApplicationFactory CreateHost(bool runtimeIdentityOutsideRequests = true) => new(fixture, services =>
    {
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(_clock);
        foreach (var worker in services.Where(x => x.ServiceType == typeof(IHostedService) &&
            (x.ImplementationType == typeof(LeasedHttpDeliveryWorker) ||
             x.ImplementationType == typeof(ShopAffiliationDeliveryWorker) ||
             x.ImplementationType == typeof(TrialActivationDeliveryWorker))).ToList())
            services.Remove(worker);
    }, runtimeIdentityOutsideRequests: runtimeIdentityOutsideRequests);

    private static HttpClient CreateClient(ApiWebApplicationFactory host) =>
        host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static async Task<Guid> CreateShopAsync(HttpClient client, bool hasTaxProfile = true)
    {
        await RegisterAsync(client);
        using var created = await client.PostAsJsonAsync("/api/shops", new
        {
            name = "P04-05 merchant", address = "42 MG Road", city = "Bengaluru", state = "Karnataka", pincode = "560001",
            taxProfile = hasTaxProfile ? new { stateCode = "29", taxRegime = "Unregistered", eInvoiceStatus = "BelowThreshold", invoiceSeriesCode = "A" } : null,
        }, Ct);
        var token = (await ReadSuccessAsync(created)).GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return Guid.Parse(new JwtSecurityToken(token).Claims.Single(x => x.Type == "active_shop_id").Value);
    }

    private static async Task RegisterAsync(HttpClient client)
    {
        using var enroll = await client.EnrollAsync(new { email = $"p0405-{Guid.NewGuid():N}@fixture.test",
            password = "Pass123!Aa", firstName = "Test", lastName = "Owner" });
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await ReadSuccessAsync(enroll)).GetProperty("accessToken").GetString());
    }

    private async Task<Binding> BindAsync(Guid shop)
    {
        await using var db = Open(Migrator);
        var membership = await db.ShopMemberships.SingleAsync(x => x.ShopId == shop, Ct);
        var binding = new Binding(shop, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var now = _clock.Now;
        foreach (var kind in new[] { "Granted", "Accepted" })
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO public.shop_affiliation_consent_events (id, shop_id, account_id, request_id,
                    actor_id, membership_id, authority_version, operation_key, kind, recorded_at, term_id,
                    shop_name, owner_name, request_expires_at)
                VALUES ({Guid.NewGuid()}, {shop}, {binding.Account}, {binding.Request},
                    {membership.UserId}, {membership.Id}, {membership.AuthorityVersion}, {Guid.NewGuid()}, {kind}, {now},
                    {binding.Term}, {"P04-05 merchant"}, {"Fixture Owner"}, {now.AddDays(15)})
                """, Ct);
        return binding;
    }

    private async Task EnforceAsync(Guid shop)
    {
        await using var db = Open(Migrator);
        Assert.Equal(1, await db.Database.ExecuteSqlAsync($"""
            UPDATE public.shop_license_states SET enforcement_activated_at = {_clock.Now}
            WHERE shop_id = {shop}
            """, Ct));
    }

    private async Task ApplyAsync(Binding binding, DateTimeOffset end, long revision = 1,
        string restriction = "Unrestricted", string state = "Active", DateTimeOffset? verified = null,
        ApplicationDbContext? connection = null, long? releasedRevision = null)
    {
        await using var owned = connection is null ? Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator)) : null;
        var db = connection ?? owned!;
        var now = _clock.Now;
        var proofTime = verified ?? now;
        var snapshot = new EntitlementSnapshotV1(1, EntitlementSnapshotsV1.PolicyVersion, Guid.NewGuid(),
            binding.Account, binding.Shop, binding.Request, new(revision, proofTime, restriction, releasedRevision),
            new(revision, proofTime, state, binding.Term, "Paid", proofTime.AddDays(-1), end, end, 2, 3));
        var body = JsonSerializer.SerializeToUtf8Bytes(snapshot, EntitlementSnapshotsV1.Json);
        var proof = new VerifiedServiceAssertion(Guid.NewGuid(), "test-key", "test-public-key", now, now.AddMinutes(1),
            SignedServiceAssertion.Digest(body), "fixture-verified-assertion");
        var fence = new ShopLicenceFence(db, new LicenceActionPolicy(), _clock);
        var result = await new EntitlementProjectionStore(db, _clock, fence).ApplyAsync(snapshot, body, proof, Ct);
        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
        var acknowledgement = JsonSerializer.Deserialize<EntitlementAcknowledgementV1>(result.Value, EntitlementSnapshotsV1.Json);
        Assert.True(acknowledgement?.Outcome == "Applied", acknowledgement?.Disposition);
    }

    private static async Task<Line> SeedGoodsAsync(HttpClient client)
    {
        var barcode = Guid.NewGuid().ToString("N");
        using var response = await client.PostAsJsonAsync("/api/inventory/inbound", new
        {
            itemName = "Fixture stock", barcode, uom = "pcs", batchNumber = "LOT", quantity = 10m,
            totalPurchaseCost = 10m, mrp = 2m, salesPrice = 2m, taxRatePercent = 0m, taxIncluded = false, purchaseTaxIncluded = false,
        }, Ct);
        return new(barcode, "LOT", "Fixture stock", 1m, 2m, 2m,
            (await ReadSuccessAsync(response)).GetProperty("inventoryBatchId").GetGuid(), "Goods", null);
    }

    private static async Task<Line> SeedServiceAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/services",
            new { name = "Repair", price = 3m, taxRatePercent = 0m, taxIncluded = false, isActive = true }, Ct);
        return new("SERVICE", "", "Repair", 0m, 3m, 0m, Guid.Empty, "Service",
            (await ReadSuccessAsync(response)).GetProperty("serviceId").GetGuid());
    }

    private async Task AssertNoPostingAsync(Guid shop, Guid? batch = null)
    {
        await using var db = Open(Migrator);
        Assert.False(await db.Sales.AnyAsync(x => x.ShopId == shop, Ct));
        Assert.False(await db.StockTransactions.AnyAsync(x => x.ShopId == shop && x.TransactionType == StockTransactionType.Out, Ct));
        Assert.False(await db.InvoiceSequences.AnyAsync(x => x.ShopId == shop, Ct));
        Assert.False(await db.CustomerLedgerEntries.AnyAsync(x => x.ShopId == shop, Ct));
        Assert.False(await db.TillSourceRecords.AnyAsync(x => x.ShopId == shop, Ct));
        Assert.False(await db.TillMovements.AnyAsync(x => x.ShopId == shop, Ct));
        if (batch is { } id)
            Assert.Equal(10m, await db.InventoryBatches.Where(x => x.Id == id).Select(x => x.Quantity).SingleAsync(Ct));
    }

    private static SalePayload SaleRequest(params Line[] lines) =>
        new($"sale-{Guid.NewGuid():N}", "Cash", lines.Sum(x => x.SalesPrice * x.Quantity), 0m, lines);

    private sealed record SalePayload(string IdempotencyKey, string PaymentMethod, decimal PaidAmount, decimal DueAmount,
        Line[] Items, Guid? CustomerId = null);

    private static async Task<JsonElement> PostSaleAsync(HttpClient client, object request)
    {
        using var response = await client.PostAsJsonAsync("/api/sales", request, Ct);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(code, (body.TryGetProperty("code", out var stableCode) ? stableCode : body.GetProperty("title")).GetString());
        }
    }

    private static async Task<JsonElement> ReadSuccessAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static ApplicationDbContext Open(string connection) => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(connection).UseSnakeCaseNamingConvention().Options);

    private sealed record Binding(Guid Shop, Guid Account, Guid Request, Guid Term);
    private sealed record Line(string Barcode, string BatchNumber, string ItemName, decimal CostPrice, decimal SalesPrice,
        decimal Mrp, Guid InventoryBatchId, string LineType, Guid? ServiceId, decimal Quantity = 1m,
        decimal TaxRatePercent = 0m, bool IsPriceIncludingTax = false);

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        private long _ticks = now.UtcTicks;
        public DateTimeOffset Now
        {
            get => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
            set => Interlocked.Exchange(ref _ticks, value.UtcTicks);
        }
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
