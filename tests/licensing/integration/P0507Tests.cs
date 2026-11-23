using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// P05-07: the provider sees a device's last known contact, last observed offline synchronization and the
/// exact known end of its offline permission. Both services run on real PostgreSQL with fixed clocks; the
/// merchant's signed outbox is the only path between them. Signing keys are generated locally.
/// </summary>
public sealed partial class P0507Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Device = "p0507 front counter";

    [Fact]
    public async Task ProviderSeesExactPermitEndAndLastContactForAnIssuedPermit()
    {
        await using var run = await Run.StartAsync(database);
        var permit = await run.IssuePermitAsync(Device);

        Assert.Equal(1L, await run.MerchantMessagesAsync());
        await run.DeliverAsync();
        using var admin = await P0417Tests.SignInAsync(run.Fixture, "support");
        run.Clock.Now = permit.NotBefore.AddHours(1);
        using var response = await admin.SendAsync(HttpMethod.Get, Route(run.Account));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var status = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(run.Clock.Now, status.GetProperty("evaluatedAt").GetDateTimeOffset());
        Assert.Equal("Unknown", status.GetProperty("disconnectedQueues").GetString());
        var device = Assert.Single(status.GetProperty("devices").EnumerateArray());
        Assert.Equal(run.Owner.ShopId, device.GetProperty("shopId").GetGuid());
        Assert.Equal(permit.ValidUntil, device.GetProperty("knownPermitEnd").GetDateTimeOffset());
        Assert.Equal(permit.NotBefore, device.GetProperty("lastPermitIssuedAt").GetDateTimeOffset());
        Assert.Equal(permit.NotBefore, device.GetProperty("lastContactAt").GetDateTimeOffset());
        Assert.True(device.GetProperty("outstanding").GetBoolean());
        Assert.Equal(1, device.GetProperty("outstandingPermits").GetInt32());
        // No upload was observed: that is stated as absent, never borrowed from the permit's time.
        Assert.Equal(JsonValueKind.Null, device.GetProperty("lastSynchronizedAt").ValueKind);
    }

    [Fact]
    public async Task ProviderSeesTheLastObservedUploadSeparatelyFromThePermit()
    {
        await using var run = await Run.StartAsync(database);
        using var admin = await P0417Tests.SignInAsync(run.Fixture, "licensing-manager");
        // Before any evidence: nothing is listed, and nothing claims the account has no offline work.
        var empty = await ReadAsync(admin, run.Account);
        Assert.Empty(empty.GetProperty("devices").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("observedSince").ValueKind);
        Assert.Equal("Unknown", empty.GetProperty("disconnectedQueues").GetString());

        var permit = await run.IssuePermitAsync(Device);
        // The fixture's merchant host reads upload receipt time from the business clock.
        run.Clock.Now = permit.NotBefore.AddHours(2).AddTicks(1230);
        var received = run.Clock.Now;
        await run.SyncAsync(Device, invoices: 3);
        Assert.Equal(received.UtcDateTime, await run.Fixture.MerchantScalarAsync(
            $"SELECT max(received_at) FROM offline_submissions WHERE shop_id = '{run.Owner.ShopId:D}'"));

        // Three invoices in one upload are one observation, and it says nothing about them.
        var sent = await run.MerchantObservationsAsync();
        Assert.True(sent.Length == 2 && sent[0].Observation == OfflineDeviceObservationsV1.PermitIssued &&
            sent[1].Observation == OfflineDeviceObservationsV1.OfflineSync, JsonSerializer.Serialize(sent) + $" permit={permit} received={received:O}");
        Assert.Equal((received, (OfflineDevicePermitWindowV1?)null), (sent[1].ObservedAt, sent[1].Permit));
        await run.DeliverAsync();

        run.Clock.Now = received.AddMinutes(5);
        var status = await ReadAsync(admin, run.Account);
        var device = Assert.Single(status.GetProperty("devices").EnumerateArray());
        Assert.Equal(received, device.GetProperty("lastSynchronizedAt").GetDateTimeOffset());
        Assert.Equal(received, device.GetProperty("lastContactAt").GetDateTimeOffset());
        Assert.Equal(permit.NotBefore, device.GetProperty("lastPermitIssuedAt").GetDateTimeOffset());
        Assert.Equal(permit.ValidUntil, device.GetProperty("knownPermitEnd").GetDateTimeOffset());
        Assert.True(device.GetProperty("outstanding").GetBoolean());
        Assert.Equal(permit.NotBefore, status.GetProperty("observedSince").GetDateTimeOffset());
        // An upload was received; whether the device still holds unsent invoices stays unknown.
        Assert.Equal("Unknown", status.GetProperty("disconnectedQueues").GetString());

        // A device that only ever uploaded has contact but no known permission window.
        await run.SyncAsync("p0507 back office");
        await run.DeliverAsync();
        var devices = (await ReadAsync(admin, run.Account)).GetProperty("devices").EnumerateArray().ToArray();
        var uploader = Assert.Single(devices, row => row.GetProperty("deviceRef").GetString() ==
            OfflineDeviceObservationsV1.DeviceRef(run.Owner.ShopId, "p0507 back office"));
        Assert.Equal((JsonValueKind.Null, false, 0), (uploader.GetProperty("knownPermitEnd").ValueKind,
            uploader.GetProperty("outstanding").GetBoolean(), uploader.GetProperty("outstandingPermits").GetInt32()));
    }

    private static string Route(Guid account, Guid? shop = null) =>
        $"/api/admin/accounts/{account:D}/device-permits" + (shop is { } id ? $"?shop={id:D}" : string.Empty);

    private static async Task<JsonElement> ReadAsync(P0110CookieBrowser admin, Guid account, Guid? shop = null)
    {
        using var response = await admin.SendAsync(HttpMethod.Get, Route(account, shop));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    internal sealed record Permit(Guid Id, Guid LeaseId, Guid GrantId, DateTimeOffset NotBefore, DateTimeOffset ValidUntil, string Reason);

    /// <summary>One licensed trading shop on the cross-service fixture, with a local offline-permit signing key.</summary>
    internal sealed class Run(P0213Fixture fixture, P0201Clock clock, Merchant owner, Guid account) : IAsyncDisposable
    {
        internal P0213Fixture Fixture { get; } = fixture;
        internal P0201Clock Clock { get; } = clock;
        internal Merchant Owner { get; } = owner;
        internal Guid Account { get; } = account;

        internal static async Task<Run> StartAsync(LicensingPostgreSqlFixture database)
        {
            using var key = RSA.Create(2048);
            var clock = new P0201Clock { Now = P0202Fixture.WholeSecondNow() };
            var fixture = await P0213Fixture.StartAsync(database, businessClock: clock, merchantExtras: new Dictionary<string, string?>
            {
                ["OfflineLicensePermit:LocalKeyId"] = "p0507-permit",
                ["OfflineLicensePermit:LocalPrivateKeyPem"] = key.ExportPkcs8PrivateKeyPem(),
            });
            var (owner, account) = await OpenShopAsync(fixture, clock.Now);
            return new(fixture, clock, owner, account);
        }

        internal static async Task<(Merchant Owner, Guid Account)> OpenShopAsync(P0213Fixture fixture, DateTimeOffset at,
            Merchant? payer = null, Guid? account = null)
        {
            var owner = await CreateTradingShopAsync(fixture.Hosts);
            var billing = account ?? await CreateAccountAsync((payer ?? owner).Client);
            var local = at.ToOffset(TimeSpan.FromHours(5.5));
            var anchor = new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, local.Offset);
            var term = await SeedTermAsync(fixture.Hosts, billing, new TermSeed(anchor.ToUniversalTime(),
                anchor.AddMonths(1).ToUniversalTime(), AnchorLocal: FormattableString.Invariant($"{anchor:yyyy-MM-dd HH:mm:ss}")));
            await P0213Fixture.LinkAsync(billing, payer ?? owner, owner, term);
            await fixture.DrainAsync(source: true);
            using var stock = await owner.Client.PostAsJsonAsync("/api/inventory/inbound", new
            {
                itemName = "Fixture stock", barcode = Guid.NewGuid().ToString("N"), uom = "pcs", batchNumber = "LOT",
                quantity = 10m, totalPurchaseCost = 10m, mrp = 2m, salesPrice = 2m, taxRatePercent = 0m,
                taxIncluded = false, purchaseTaxIncluded = false,
            });
            Assert.True(stock.IsSuccessStatusCode, await stock.Content.ReadAsStringAsync());
            return (owner, billing);
        }

        internal Task<Permit> IssuePermitAsync(string device) => IssuePermitAsync(Owner, device);

        internal static async Task<Permit> IssuePermitAsync(Merchant owner, string device)
        {
            var (lease, grant) = await ReserveAsync(owner, device);
            return await CompleteSnapshotAsync(owner, device, lease, grant);
        }

        internal static async Task<(Guid Lease, Guid Grant)> ReserveAsync(Merchant owner, string device)
        {
            using var reserved = await owner.Client.PostAsJsonAsync("/api/sales/invoice-leases/reserve", new { deviceId = device, blockSize = 25 });
            Assert.True(reserved.IsSuccessStatusCode, await reserved.Content.ReadAsStringAsync());
            var body = await reserved.Content.ReadFromJsonAsync<JsonElement>();
            return (body.GetProperty("leaseId").GetGuid(), body.GetProperty("licenceEvidence").GetProperty("grantId").GetGuid());
        }

        internal static async Task<Permit> CompleteSnapshotAsync(Merchant owner, string device, Guid lease, Guid grant)
        {
            var record = await StreamAsync(owner, device, lease, grant);
            Assert.True(record.GetProperty("status").GetString() == "issued", record.GetRawText());
            return new(record.GetProperty("permitId").GetGuid(), lease, grant, record.GetProperty("notBefore").GetDateTimeOffset(),
                record.GetProperty("validUntil").GetDateTimeOffset(), record.GetProperty("limitingReason").GetString()!);
        }

        internal static async Task<JsonElement> StreamAsync(Merchant owner, string device, Guid lease, Guid grant)
        {
            using var stream = await owner.Client.GetAsync(
                $"/api/sales/offline-snapshot/stream?leaseId={lease:D}&grantId={grant:D}&deviceId={Uri.EscapeDataString(device)}");
            var text = await stream.Content.ReadAsStringAsync();
            Assert.True(stream.StatusCode == HttpStatusCode.OK, text);
            return text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement)
                .Single(line => line.GetProperty("type").GetString() == "licencePermit").GetProperty("licencePermit").Clone();
        }

        /// <summary>Uploads structurally empty invoices: the server retains each envelope, then refuses to post it.</summary>
        internal async Task SyncAsync(string device, int invoices = 1)
        {
            var sales = Enumerable.Range(0, invoices).Select(_ => new
            {
                clientSaleId = Guid.NewGuid().ToString("N"), invoiceNumber = "A/2627/9" + Random.Shared.Next(10000, 99999),
                soldAt = MerchantClock.Now, customerName = "Walk-in Meera", paymentMethod = 0, paidAmount = 0m, dueAmount = 0m,
                subtotalBeforeDiscount = 0m, totalBeforeDiscount = 0m, totalDiscountAmount = 0m, totalTaxAmount = 0m,
                totalAmount = 0m, saleDiscountOverrideType = 0, saleDiscountOverrideValue = 0m, items = Array.Empty<object>(),
            }).ToArray();
            using var response = await Owner.Client.PostAsJsonAsync("/api/sales/offline-sync", new { deviceId = device, sales });
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }

        internal P0201Clock MerchantClock => Fixture.Hosts.Fixture.Clock;

        /// <summary>Runs the merchant's signed outbox once its messages are due on the delivery clock.</summary>
        internal async Task DeliverAsync()
        {
            var latest = MerchantClock.Now > Clock.Now ? MerchantClock.Now : Clock.Now;
            if (Fixture.Transport.LeaseClock.Now <= latest)
                Fixture.Transport.LeaseClock.Now = latest.AddSeconds(1);
            await Fixture.DrainAsync(source: false);
        }

        internal async Task<OfflineDeviceObservationV1[]> MerchantObservationsAsync()
        {
            await using var connection = new Npgsql.NpgsqlConnection(Fixture.MerchantDb.ConnectionString);
            await connection.OpenAsync();
            await using var command = new Npgsql.NpgsqlCommand($"""
                SELECT m.payload FROM entitlement_messages m JOIN entitlement_outbox o USING (event_id)
                WHERE m.kind = 'DeviceObservation' AND m.shop_id = '{Owner.ShopId:D}'
                """, connection);
            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<OfflineDeviceObservationV1>();
            while (await reader.ReadAsync())
                rows.Add(JsonSerializer.Deserialize<OfflineDeviceObservationV1>((byte[])reader[0], EntitlementSnapshotsV1.Json)!);
            return [.. rows.OrderBy(row => row.ObservedAt)];
        }

        /// <summary>A merchant-signed observation posted straight to licensing, as the outbox would.</summary>
        internal async Task<HttpResponseMessage> PostAsync(OfflineDeviceObservationV1 observation)
        {
            var body = JsonSerializer.SerializeToUtf8Bytes(observation, EntitlementSnapshotsV1.Json);
            using var request = await Fixture.Transport.SignedAsync(body, Fixture.AuthClock, merchant: true,
                path: OfflineDeviceObservationsV1.Path);
            return await Fixture.LicensingApi.SendAsync(request);
        }

        internal Task<long> SourceCountAsync(string table, string where = "true") =>
            CountAsync(Fixture, table, where);

        internal static async Task<long> CountAsync(P0213Fixture fixture, string table, string where = "true") =>
            (long)(await fixture.SourceScalarAsync($"SELECT count(*) FROM public.{table} WHERE {where}"))!;

        internal async Task<long> MerchantMessagesAsync(string status = "") => (long)(await Fixture.MerchantScalarAsync($"""
            SELECT count(*) FROM entitlement_messages m JOIN entitlement_outbox o USING (event_id)
            WHERE m.kind = 'DeviceObservation' AND m.shop_id = '{Owner.ShopId:D}' AND ('{status}' = '' OR o.status = '{status}')
            """))!;

        public ValueTask DisposeAsync() => Fixture.DisposeAsync();
    }
}
