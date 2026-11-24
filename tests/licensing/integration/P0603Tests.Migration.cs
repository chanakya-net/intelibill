using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0603Tests
{
    private const string PreviousLicensingMigration = "20261124130000_PaidCapacityIncreases";

    [Fact]
    public async Task ScheduledReductionMigrationPreservesEvidenceAndRefusesDowngrade()
    {
        await using var fixture = await StartAsync(database, January31);
        var package = await fixture.PackageAsync(2, ShopNames);
        var (_, term) = await PurchaseAsync(package, "Month");
        var before = await EvidenceAsync(fixture, term);

        await using (var context = fixture.Hosts.Fixture.OpenMigrationContext())
        {
            Assert.False(context.Database.HasPendingModelChanges());
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousLicensingMigration);
            Assert.True(await fixture.ScalarAsync("SELECT to_regclass('public.scheduled_capacity_reductions')::text") is null or DBNull);
            await migrator.MigrateAsync();
            Assert.Equal(true, await fixture.ScalarAsync(
                "SELECT relforcerowsecurity FROM pg_class WHERE relname = 'scheduled_capacity_reductions' AND relnamespace = 'public'::regnamespace"));
        }

        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, P0302Fixture.RuntimeGrant);
        Assert.Equal(before, await EvidenceAsync(fixture, term));

        var solo = await PublishReductionPlanAsync(fixture, "single-shop", "Solo Migration", 1, 3, 6000L);
        fixture.At(February14);
        var preview = await CreatePreviewAsync(package, term, solo, 1, 3, 5, February29);
        await ScheduleAsync(package, term, preview, [package.Shops[0].ShopId]);

        await using (var context = fixture.Hosts.Fixture.OpenMigrationContext())
        {
            Assert.Equal("off", await context.Database.SqlQueryRaw<string>(
                "SELECT current_setting('is_superuser') AS \"Value\"").SingleAsync());
            var refused = await Record.ExceptionAsync(() => context.GetService<IMigrator>().MigrateAsync(PreviousLicensingMigration));
            Assert.Equal("55000", Assert.IsType<PostgresException>(refused?.GetBaseException()).SqlState);
        }

        foreach (var statement in new[]
        {
            "UPDATE public.capacity_reduction_previews SET amount_paise = 1",
            "DELETE FROM public.scheduled_capacity_reductions",
            "TRUNCATE public.scheduled_capacity_reduction_shops CASCADE",
        })
            Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() =>
                LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, statement))).SqlState);

        foreach (var merchantTable in new[] { "reduction_manifest_pages", "reduction_manifest_shops", "reduction_reservation_receipts", "applied_shop_reductions" })
            Assert.Equal(true, await fixture.Checkout.Delivery.MerchantScalarAsync(
                $"SELECT relforcerowsecurity FROM pg_class WHERE relname = '{merchantTable}' AND relnamespace = 'public'::regnamespace"));
    }

    [Fact]
    public void CapacityReductionContractPinsHashesStatesBoundsAndTransportSizes()
    {
        var contractPath = Path.Combine(ProjectPaths.Root, "tests/contracts/licensing/capacity-reductions-v1.json");
        using var contract = JsonDocument.Parse(File.ReadAllText(contractPath));
        var root = contract.RootElement;

        Assert.Equal(CapacityReductionsV1.Policy, root.GetProperty("policy").GetString());
        Assert.Equal(CapacityReductionsV1.States,
            root.GetProperty("states").EnumerateArray().Select(e => e.GetString()!).ToArray());
        var actions = root.GetProperty("actions");
        Assert.Equal(CapacityReductionsV1.Preview, actions.GetProperty("preview").GetString());
        Assert.Equal(CapacityReductionsV1.PreviewShops, actions.GetProperty("previewShops").GetString());
        Assert.Equal(CapacityReductionsV1.Schedule, actions.GetProperty("schedule").GetString());
        Assert.Equal(CapacityReductionsV1.Read, actions.GetProperty("read").GetString());
        Assert.Equal(CapacityReductionsV1.Recover, actions.GetProperty("recover").GetString());
        Assert.Equal(CapacityReductionsV1.Shops, actions.GetProperty("shops").GetString());
        Assert.Equal(CapacityReductionsV1.Resolve, actions.GetProperty("resolve").GetString());

        var codes = root.GetProperty("errorCodes");
        Assert.Equal(CapacityReductionsV1.Invalid, codes.GetProperty("invalid").GetString());
        Assert.Equal(CapacityReductionsV1.NotFound, codes.GetProperty("notFound").GetString());
        Assert.Equal(CapacityReductionsV1.SourceChanged, codes.GetProperty("sourceChanged").GetString());
        Assert.Equal(CapacityReductionsV1.Expired, codes.GetProperty("expired").GetString());
        Assert.Equal(CapacityReductionsV1.MemberConflict, codes.GetProperty("memberConflict").GetString());
        Assert.Equal(CapacityReductionsV1.AlreadyScheduled, codes.GetProperty("alreadyScheduled").GetString());
        Assert.Equal(CapacityReductionsV1.AlreadyFunded, codes.GetProperty("alreadyFunded").GetString());
        Assert.Equal(CapacityReductionsV1.NeedsReview, codes.GetProperty("needsReview").GetString());

        var bounds = root.GetProperty("bounds");
        Assert.Equal(CapacityReductionsV1.PageSize, bounds.GetProperty("pageSize").GetInt32());
        Assert.Equal(CapacityReductionsV1.MaxInlineConflicts, bounds.GetProperty("maxInlineConflicts").GetInt32());
        Assert.Equal(ReductionTransportV1.ObservePageSize, bounds.GetProperty("observePageSize").GetInt32());
        Assert.Equal(ReductionTransportV1.ManifestPageSize, bounds.GetProperty("manifestPageSize").GetInt32());
        Assert.Equal(ReductionTransportV1.BodyLimit, bounds.GetProperty("transportBodyLimit").GetInt32());

        // Worst-case 25-shop observation & manifest payloads MUST stay strictly within 8,192 bytes.
        var maxHash = new string('a', 64);
        var observeReq = new ReductionObservationRequestV1(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, 4,
            P0209Fixture.Utc("2028-02-29T04:30:00Z"),
            Enumerable.Range(0, ReductionTransportV1.ObservePageSize)
                .Select(_ => new ReductionExpectedShopV1(Guid.NewGuid(), Guid.NewGuid())).ToArray());
        Assert.InRange(JsonSerializer.SerializeToUtf8Bytes(observeReq, EntitlementSnapshotsV1.Json).Length,
            1, ReductionTransportV1.BodyLimit);

        var observeRes = new ReductionObservationResponseV1(1, observeReq.AccountId, observeReq.TermId, observeReq.Challenge,
            0, 4, P0209Fixture.Utc("2028-02-29T04:30:00Z"),
            observeReq.Shops.Select(s => new ReductionObservedShopV1(s.ShopId, s.AffiliationRequestId, 100000, maxHash, true)).ToArray());
        Assert.InRange(JsonSerializer.SerializeToUtf8Bytes(observeRes, EntitlementSnapshotsV1.Json).Length,
            1, ReductionTransportV1.BodyLimit);

        var manifestPage = new ReductionManifestPageV1(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            observeReq.AccountId, observeReq.TermId, observeReq.Shops[0].ShopId, 99, 0, 4, 100, 50,
            maxHash, maxHash, P0209Fixture.Utc("2028-02-29T04:35:00Z"),
            observeReq.Shops.Select(s => new ReductionManifestShopV1(s.ShopId, s.AffiliationRequestId, true, 100000, maxHash)).ToArray());
        Assert.InRange(JsonSerializer.SerializeToUtf8Bytes(manifestPage, EntitlementSnapshotsV1.Json).Length,
            1, ReductionTransportV1.BodyLimit);

        // Pin exact binding & observation hash vectors.
        var vec = root.GetProperty("hashVectors");
        var shopA = vec.GetProperty("shopA").GetGuid();
        var affA = vec.GetProperty("affiliationA").GetGuid();
        var shopB = vec.GetProperty("shopB").GetGuid();
        var affB = vec.GetProperty("affiliationB").GetGuid();
        Assert.Equal(vec.GetProperty("expectedBindingDigest").GetString(),
            ReductionTransportV1.BindingDigest([(shopB, affB), (shopA, affA)]));
        Assert.Equal(vec.GetProperty("expectedObservationDigest").GetString(),
            ReductionTransportV1.ObservationDigest([(shopB, affB, 4, maxHash), (shopA, affA, 1, maxHash)]));
    }
}
