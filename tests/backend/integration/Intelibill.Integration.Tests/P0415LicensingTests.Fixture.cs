using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Billing;
using Intelibill.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed partial class P0415LicensingTests(PostgreSqlTestFixture fixture)
{
    private readonly TestClock _clock = new(EntitlementSnapshotsV1.Instant(TimeProvider.System));
    private static CancellationToken Ct => CancellationToken.None;

    private ApiWebApplicationFactory CreateHost(RSA key, Action<IServiceCollection>? configure = null, bool productionLockBudgets = false) =>
        new(fixture, services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(_clock);
            services.AddSingleton<IOptionsMonitor<EntitlementTransportOptions>>(new FixtureTransportOptions(new()
            {
                Enabled = true,
                PeerOrigin = "https://licensing.fixture.test",
                LocalKeyId = "p0415-merchant",
                PublicKeys = new() { ["p0415-source"] = key.ExportSubjectPublicKeyInfoPem() },
            }));
            foreach (var worker in services.Where(x => x.ServiceType == typeof(IHostedService) &&
                (x.ImplementationType == typeof(LeasedHttpDeliveryWorker) ||
                 x.ImplementationType == typeof(ShopAffiliationDeliveryWorker) ||
                 x.ImplementationType == typeof(TrialActivationDeliveryWorker))).ToList())
                services.Remove(worker);
            configure?.Invoke(services);
        }, runtimeIdentityOutsideRequests: true, productionLockBudgets: productionLockBudgets);

    private static HttpClient CreateClient(ApiWebApplicationFactory host) =>
        host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });

    private static async Task<Guid> CreateShopAsync(HttpClient client)
    {
        using var enrollment = await client.EnrollAsync(new
        {
            email = $"p0415-{Guid.NewGuid():N}@example.test", password = "Pass123!Aa", firstName = "Test", lastName = "Owner",
        });
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await ReadSuccessAsync(enrollment)).GetProperty("accessToken").GetString());
        using var created = await client.PostAsJsonAsync("/api/shops", new
        {
            name = "P04-15 merchant", address = "42 MG Road", city = "Bengaluru", state = "Karnataka", pincode = "560001",
            taxProfile = new { stateCode = "29", taxRegime = "Unregistered", eInvoiceStatus = "BelowThreshold", invoiceSeriesCode = "A" },
        }, Ct);
        var token = (await ReadSuccessAsync(created)).GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return Guid.Parse(new JwtSecurityToken(token).Claims.Single(x => x.Type == "active_shop_id").Value);
    }

    private async Task<EntitlementSnapshotV1> BindAsync(Guid shop, Guid? accountId = null)
    {
        await using var db = OpenMigrator();
        var membership = await db.ShopMemberships.SingleAsync(x => x.ShopId == shop, Ct);
        var account = accountId ?? Guid.NewGuid();
        var request = Guid.NewGuid();
        var term = Guid.NewGuid();
        var now = _clock.Now;
        foreach (var kind in new[] { "Granted", "Accepted" })
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO public.shop_affiliation_consent_events (id, shop_id, account_id, request_id,
                    actor_id, membership_id, authority_version, operation_key, kind, recorded_at, term_id,
                    shop_name, owner_name, request_expires_at)
                VALUES ({Guid.NewGuid()}, {shop}, {account}, {request},
                    {membership.UserId}, {membership.Id}, {membership.AuthorityVersion}, {Guid.NewGuid()}, {kind}, {now},
                    {term}, {"P04-15 merchant"}, {"Fixture Owner"}, {now.AddDays(15)})
                """, Ct);

        return new(1, EntitlementSnapshotsV1.PolicyVersion, Guid.NewGuid(), account, shop, request,
            new(1, now, "Unrestricted", null),
            new(1, now, "Active", term, "Paid", now.AddDays(-1), now.AddHours(1), now.AddHours(1), 2, 3));
    }

    private async Task EnforceAsync(Guid shop)
    {
        await using var db = OpenMigrator();
        Assert.Equal(1, await db.Database.ExecuteSqlAsync($"""
            UPDATE public.shop_license_states SET enforcement_activated_at = {_clock.Now} WHERE shop_id = {shop}
            """, Ct));
    }

    private async Task DeliverAsync(HttpClient client, RSA key, EntitlementSnapshotV1 snapshot)
    {
        var path = EntitlementSnapshotsV1.SnapshotPath;
        var body = JsonSerializer.SerializeToUtf8Bytes(snapshot, EntitlementSnapshotsV1.Json);
        var token = await SignedServiceAssertion.SignAsync(SignedServiceAssertion.LicensingDelivery, "p0415-source", path,
            body, _clock, (input, _) => ValueTask.FromResult(key.SignData(input, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)), Ct);
        using var response = await LeasedHttpDeliveryPump.PostAsync(client, client.BaseAddress!, path, body, token, Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        var ack = await response.Content.ReadFromJsonAsync<EntitlementAcknowledgementV1>(EntitlementSnapshotsV1.Json, Ct);
        Assert.NotNull(ack);
        Assert.Equal("Applied", ack.Outcome);
        Assert.Equal("Applied", ack.Disposition);
        Assert.Equal(snapshot.Account?.Revision, ack.AccountRevision);
        Assert.Equal(snapshot.Shop?.Revision, ack.ShopRevision);
        Assert.Equal(snapshot.EventId, ack.EventId);
        Assert.Equal(SignedServiceAssertion.Digest(body), ack.Digest);
        Assert.Equal(snapshot.Account?.Revision, ack.AppliedAccountRevision);
        Assert.Equal(snapshot.Shop?.Revision, ack.AppliedShopRevision);

        await using var db = OpenMigrator();
        Assert.True(await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT 1 FROM public.entitlement_snapshot_inbox i
                JOIN public.entitlement_outbox o ON o.event_id = i.event_id
                WHERE i.event_id = {ack.EventId} AND i.digest = {ack.Digest}
            ) AS "Value"
            """).SingleAsync(Ct));
    }

    private static async Task<Guid> CreateSaleAsync(HttpClient client, Guid? customerId = null, string? key = null)
    {
        var barcode = Guid.NewGuid().ToString("N");
        using var stock = await client.PostAsJsonAsync("/api/inventory/inbound", new
        {
            itemName = "Fixture stock", barcode, uom = "pcs", batchNumber = "LOT", quantity = 10m,
            totalPurchaseCost = 10m, mrp = 2m, salesPrice = 2m, taxRatePercent = 0m, taxIncluded = false, purchaseTaxIncluded = false,
        }, Ct);
        var batch = (await ReadSuccessAsync(stock)).GetProperty("inventoryBatchId").GetGuid();
        using var sale = await client.PostAsJsonAsync("/api/sales", new
        {
            idempotencyKey = key ?? $"sale-{Guid.NewGuid():N}", customerId,
            paymentMethod = (int)(customerId.HasValue ? PaymentMethod.Credit : PaymentMethod.Cash),
            paidAmount = customerId.HasValue ? 0m : 2m, dueAmount = customerId.HasValue ? 2m : 0m,
            items = new[] { new { barcode, batchNumber = "LOT", itemName = "Fixture stock", quantity = 1m,
                costPrice = 1m, salesPrice = 2m, mrp = 2m, taxRatePercent = 0m, isPriceIncludingTax = false, inventoryBatchId = batch } },
        }, Ct);
        return (await ReadSuccessAsync(sale)).GetProperty("saleId").GetGuid();
    }

    private ApplicationDbContext OpenMigrator() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(fixture.MigratorConnectionString).UseSnakeCaseNamingConvention().Options);

    private static async Task<JsonElement> ReadSuccessAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

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

    private sealed class FixtureTransportOptions(EntitlementTransportOptions options) : IOptionsMonitor<EntitlementTransportOptions>
    {
        public EntitlementTransportOptions CurrentValue => options;
        public EntitlementTransportOptions Get(string? name) => options;
        public IDisposable? OnChange(Action<EntitlementTransportOptions, string?> listener) => null;
    }
}
