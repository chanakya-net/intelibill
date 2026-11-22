using System.Diagnostics.Metrics;
using System.Net;
using System.Security.Cryptography;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

public sealed partial class P0402LicensingTests
{
    [Fact]
    public async Task Minute_scan_is_bounded_scoped_and_coalesces_racing_workers()
    {
        using var key = RSA.Create(2048);
        using var host = MerchantHost(key, new());
        await host.InitializeAsync();
        await GrantDeliveryWorkerAsync();
        var warm = await SeedAsync();
        await using (var projection = CreateDb())
            await ApplyAsync(projection, warm, VerifiedAt, VerifiedAt, VerifiedAt.AddDays(10));
        for (var index = 0; index < 20; index++)
            await SeedAsync();

        await using var owner = CreateDb(owner: true);
        await using (var scoped = CreateDb())
        {
            await using var transaction = await BeginScopedAsync(scoped, warm.ShopId);
            Assert.Equal(0, await DueCountAsync(scoped));
        }

        var now = _clock.Now;
        Assert.Equal(20, await DueCountAsync(owner));
        await ScanAsync(host);
        Assert.Equal(20, await RequestCountAsync(owner));
        await Task.WhenAll(ScanAsync(host), ScanAsync(host));
        Assert.Equal(21, await RequestCountAsync(owner));
        var dueAt = await owner.Database.SqlQuery<DateTimeOffset>(
            $"SELECT next_check_at AS \"Value\" FROM public.entitlement_reconciliation_requests WHERE shop_id = {warm.ShopId}").SingleAsync(Ct);
        Assert.InRange(dueAt, now.AddSeconds(50), now.AddSeconds(55));

        _clock.Now = dueAt.AddMicroseconds(-1);
        await MarkRequestsDeliveredAsync(owner);
        await ScanAsync(host);
        Assert.Equal(1, await RequestCountAsync(owner, warm.ShopId));
        _clock.Now = dueAt;
        await Task.WhenAll(ScanAsync(host), ScanAsync(host));
        Assert.Equal(2, await RequestCountAsync(owner, warm.ShopId));
        Assert.Equal(VerifiedAt, (await ReadProofAsync(warm)).ShopAt);
    }

    [Fact]
    public async Task Upgrade_clamps_the_legacy_hour_and_preserves_the_delivered_request()
    {
        await fixture.ResetDatabaseAsync();
        var binding = await SeedAsync();
        await using var owner = CreateDb(owner: true);
        await GrantDeliveryWorkerAsync();
        var migrator = owner.GetService<IMigrator>();
        await migrator.MigrateAsync("20261118080000_AddLicenceEnforcementFences", Ct);
        var queuedAt = _clock.Now;
        var eventId = await SeedLegacyRequestAsync(owner, binding, queuedAt);

        await migrator.MigrateAsync(cancellationToken: Ct);
        await using var runtime = CreateDb();
        _clock.Now = queuedAt.AddSeconds(55).AddMicroseconds(-1);
        Assert.Equal(0, await DueCountAsync(runtime));
        _clock.Now = queuedAt.AddSeconds(55);
        Assert.Equal(1, await DueCountAsync(runtime));
        Assert.Equal(eventId, await owner.Database.SqlQuery<Guid>($"""
            SELECT event_id AS "Value" FROM public.entitlement_reconciliation_requests WHERE shop_id = {binding.ShopId}
            """).SingleAsync(Ct));
        Assert.Equal("Delivered", await owner.Database.SqlQuery<string>($"""
            SELECT status AS "Value" FROM public.entitlement_outbox WHERE event_id = {eventId}
            """).SingleAsync(Ct));
    }

    private static async Task<Guid> SeedLegacyRequestAsync(ApplicationDbContext db, Binding binding, DateTimeOffset queuedAt)
    {
        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        var request = new EntitlementReconciliationV1(1, Guid.NewGuid(), binding.AccountId, binding.ShopId,
            binding.RequestId, null, null);
        var body = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(request, EntitlementSnapshotsV1.Json);
        await new LeasedHttpOutbox(db).AppendAsync(request.EventId, binding.AccountId, binding.ShopId, "Reconciliation",
            body, SignedServiceAssertion.Digest(body), queuedAt, Ct);
        await MarkRequestsDeliveredAsync(db);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.entitlement_reconciliation_requests (shop_id, event_id, next_check_at)
            VALUES ({binding.ShopId}, {request.EventId}, {queuedAt.AddHours(1)})
            """, Ct);
        await tx.CommitAsync(Ct);
        return request.EventId;
    }

    [Fact]
    public async Task Only_confirmed_applied_receipts_measure_acknowledgement_lag()
    {
        _clock.Now = EntitlementSnapshotsV1.Instant(TimeProvider.System);
        using var sourceKey = RSA.Create(2048);
        using var merchantKey = RSA.Create(2048);
        var transport = new FixtureTransport();
        using var host = MerchantHost(sourceKey, transport, merchantKey);
        await host.InitializeAsync();
        await GrantDeliveryWorkerAsync();
        using var client = CreateClient(host);
        var (shop, _) = await EnrollShopAsync(client);
        var binding = await BindShopAsync(shop);
        var verified = _clock.Now;
        var samples = new List<double>();
        using var listener = ObserveAcknowledgements(samples);

        await ScanAsync(host);
        Assert.True(await DeliverOneAsync(host)); // Outage, retaining the signed request for retry.
        Assert.Empty(samples);
        Assert.Equal(1, transport.Calls);
        var receivedPaths = new List<string>();
        transport.Respond = request => AcceptSignedAsync(request, merchantKey, receivedPaths);
        _clock.Now = verified.AddSeconds(4);
        Assert.True(await DeliverOneAsync(host)); // Reconciliation accepted.
        Assert.Empty(samples);
        await AssertTradingAsync(binding, _clock.Now, Application.Common.Licensing.LicenceOutcome.Blocked,
            "Licence.VerificationUnavailable");
        var receipt = await PostSnapshotAsync(client, sourceKey, SnapshotBody(binding, verified, verified.AddDays(10)));
        Assert.Empty(samples);

        _clock.Now = verified.AddSeconds(9);
        Assert.True(await DeliverOneAsync(host));
        Assert.Equal(new[] { EntitlementSnapshotsV1.ReconciliationPath, EntitlementSnapshotsV1.AcknowledgementPath }, receivedPaths);
        Assert.Equal(9d, Assert.Single(samples));
        await AssertDuplicateAcknowledgementAsync(host, receipt);
        Assert.Single(samples);
    }

    private static MeterListener ObserveAcknowledgements(List<double> samples)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, active) =>
        {
            if (instrument.Meter.Name == "InteliBill.Application" &&
                instrument.Name == "intelibill.licensing.entitlement.acknowledgement_lag")
            {
                Assert.Equal("s", instrument.Unit);
                active.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
        {
            Assert.True(tags.IsEmpty);
            samples.Add(value);
        });
        listener.Start();
        return listener;
    }

    private async Task<HttpResponseMessage> AcceptSignedAsync(HttpRequestMessage request, RSA merchantKey, List<string> paths)
    {
        var path = request.RequestUri!.AbsolutePath;
        var body = await request.Content!.ReadAsByteArrayAsync(Ct);
        Assert.NotNull(SignedServiceAssertion.Verify(request.Headers.Authorization?.Parameter,
            SignedServiceAssertion.MerchantEvents, path, body, _clock, _ => merchantKey.ExportSubjectPublicKeyInfoPem()));
        paths.Add(path);
        return new HttpResponseMessage(HttpStatusCode.NoContent);
    }

    private static async Task<bool> DeliverOneAsync(ApiWebApplicationFactory host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LeasedHttpDeliveryPump>().DeliverOneAsync(Ct);
    }

    private static async Task AssertDuplicateAcknowledgementAsync(ApiWebApplicationFactory host, byte[] receipt)
    {
        var ack = System.Text.Json.JsonSerializer.Deserialize<EntitlementAcknowledgementV1>(receipt, EntitlementSnapshotsV1.Json)!;
        await using var scope = host.Services.CreateAsyncScope();
        var destination = scope.ServiceProvider.GetRequiredService<ILeasedHttpDestination>();
        Assert.Null(await destination.SendAsync(new(ack.EventId, "Acknowledgement", receipt, 2), Ct));
    }

    private async Task GrantDeliveryWorkerAsync()
    {
        await using var owner = CreateDb(owner: true);
        await owner.Database.ExecuteSqlRawAsync("""
            GRANT EXECUTE ON FUNCTION public.due_merchant_entitlement_reconciliation(timestamptz) TO intelibill_app;
            GRANT EXECUTE ON FUNCTION public.claim_entitlement_delivery(timestamptz, uuid) TO intelibill_app;
            GRANT EXECUTE ON FUNCTION public.finish_entitlement_delivery(uuid, uuid, timestamptz, boolean, boolean, text) TO intelibill_app;
            """, Ct);
    }

    private Task<int> DueCountAsync(ApplicationDbContext db) =>
        db.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM public.due_merchant_entitlement_reconciliation({_clock.Now})
            """).SingleAsync(Ct);

    private static Task<int> RequestCountAsync(ApplicationDbContext db, Guid? shop = null) =>
        db.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM public.entitlement_messages
            WHERE kind = 'Reconciliation' AND ({shop}::uuid IS NULL OR shop_id = {shop})
            """).SingleAsync(Ct);

    private static Task<int> MarkRequestsDeliveredAsync(ApplicationDbContext db) =>
        db.Database.ExecuteSqlRawAsync("""
            UPDATE public.entitlement_outbox o SET status = 'Delivered'
            FROM public.entitlement_messages m WHERE m.event_id = o.event_id AND m.kind = 'Reconciliation'
            """, Ct);

    private static async Task ScanAsync(ApiWebApplicationFactory host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<LeasedHttpDeliveryPump>().QueueDueAsync(Ct);
    }
}
