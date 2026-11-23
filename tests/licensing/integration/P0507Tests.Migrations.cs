using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

// Its own databases, so no earlier case supplies downgrade-blocking evidence.
public sealed class P0507TestsMigrations(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string SourcePredecessor = "20261121120000_OperationalClosureReopenings";
    private const string MerchantPredecessor = "20261123030720_AddLicenseHistoryReviewEvidence";

    [Fact]
    public async Task UpgradeRetainsEvidenceAndDowngradeRefusesWhileEvidenceExists()
    {
        await using var run = await P0507Tests.Run.StartAsync(database);
        await using var source = run.Fixture.Hosts.Fixture.OpenMigrationContext();
        await using var merchant = MerchantDb(database);
        Assert.False(source.Database.HasPendingModelChanges());
        Assert.False(merchant.Database.HasPendingModelChanges());

        // With no device evidence each migration goes down and comes back, restoring the earlier constraint between.
        await source.GetService<IMigrator>().MigrateAsync(SourcePredecessor);
        await merchant.GetService<IMigrator>().MigrateAsync(MerchantPredecessor);
        Assert.DoesNotContain("DeviceObservation", await KindCheckAsync(source, "entitlement_merchant_inbox"));
        Assert.DoesNotContain("DeviceObservation", await KindCheckAsync(merchant, "entitlement_messages"));
        await source.Database.MigrateAsync();
        await merchant.Database.MigrateAsync();
        await source.Database.ExecuteSqlRawAsync("GRANT SELECT, INSERT ON public.offline_device_observations TO licensing_runtime");
        Assert.Contains("DeviceObservation", await KindCheckAsync(source, "entitlement_merchant_inbox"));
        Assert.Contains("DeviceObservation", await KindCheckAsync(merchant, "entitlement_messages"));

        var permit = await run.IssuePermitAsync("p0507 migration till");
        await run.SyncAsync("p0507 migration till");
        await run.DeliverAsync();
        Assert.Equal(2L, await run.SourceCountAsync("offline_device_observations"));

        foreach (var (context, predecessor) in new (DbContext, string)[] { (source, SourcePredecessor), (merchant, MerchantPredecessor) })
        {
            var failure = await Record.ExceptionAsync(() => context.GetService<IMigrator>().MigrateAsync(predecessor));
            Assert.Equal("55000", Assert.IsType<PostgresException>(failure?.GetBaseException()).SqlState);
        }
        Assert.Equal(2L, await run.SourceCountAsync("offline_device_observations", $"shop_id = '{run.Owner.ShopId:D}'"));
        Assert.Equal(permit.ValidUntil.UtcDateTime, await run.Fixture.SourceScalarAsync(
            $"SELECT valid_until FROM offline_device_observations WHERE permit_id = '{permit.Id:D}'"));
        foreach (var (connection, table) in new[] { (run.Fixture.Hosts.LicensingOwner, "offline_device_observations"),
            (run.Fixture.Hosts.LicensingOwner, "entitlement_merchant_inbox"), (run.Fixture.MerchantDb, "entitlement_messages") })
            Assert.Equal(true, await LicensingPostgreSqlFixture.ReadScalarAsync(connection,
                $"SELECT relforcerowsecurity FROM pg_class WHERE relname = '{table}' AND relnamespace = 'public'::regnamespace"));
    }

    private static async Task<string> KindCheckAsync(DbContext context, string table)
    {
        var name = table + "_kind_check";
        return await context.Database.SqlQuery<string>($"""
            SELECT pg_get_constraintdef(c.oid) AS "Value" FROM pg_constraint c JOIN pg_class t ON t.oid = c.conrelid
            WHERE t.relname = {table} AND t.relnamespace = 'public'::regnamespace AND c.conname = {name}
            """).SingleAsync();
    }
}
