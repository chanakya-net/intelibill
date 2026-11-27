using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Integration.Tests;

// Its own databases, so the database-wide observation counts see only this case.
public sealed class P0507TestsMigrations(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task DeviceEvidenceIsForcedRlsAndHiddenFromTheMigrationIdentity()
    {
        await using var run = await P0507Tests.Run.StartAsync(database);
        await using var source = run.Fixture.Hosts.Fixture.OpenMigrationContext();

        var permit = await run.IssuePermitAsync("p0507 migration till");
        await run.SyncAsync("p0507 migration till");
        await run.DeliverAsync();
        Assert.Equal(2L, await run.SourceCountAsync("offline_device_observations"));
        Assert.Equal(2L, await run.SourceCountAsync("offline_device_observations", $"shop_id = '{run.Owner.ShopId:D}'"));
        Assert.Equal(permit.ValidUntil.UtcDateTime, await run.Fixture.SourceScalarAsync(
            $"SELECT valid_until FROM offline_device_observations WHERE permit_id = '{permit.Id:D}'"));
        foreach (var (connection, table) in new[] { (run.Fixture.Hosts.LicensingOwner, "offline_device_observations"),
            (run.Fixture.Hosts.LicensingOwner, "entitlement_merchant_inbox"), (run.Fixture.MerchantDb, "entitlement_messages") })
            Assert.Equal(true, await LicensingPostgreSqlFixture.ReadScalarAsync(connection,
                $"SELECT relforcerowsecurity FROM pg_class WHERE relname = '{table}' AND relnamespace = 'public'::regnamespace"));

        // The licensing migrator is a non-superuser NOBYPASSRLS owner, so forced RLS hides observations from it.
        Assert.Equal("off", await source.Database.SqlQueryRaw<string>(
            "SELECT current_setting('is_superuser') AS \"Value\"").SingleAsync());
        Assert.False(await source.Database.SqlQueryRaw<bool>(
            "SELECT rolbypassrls AS \"Value\" FROM pg_roles WHERE rolname = current_user").SingleAsync());
        Assert.Equal(0L, await source.Database.SqlQueryRaw<long>(
            "SELECT count(*) AS \"Value\" FROM public.offline_device_observations").SingleAsync());
    }
}
