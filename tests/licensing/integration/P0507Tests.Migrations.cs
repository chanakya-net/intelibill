using Intelibill.Infrastructure.Data;
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
        await source.Database.ExecuteSqlRawAsync("""
            GRANT SELECT, INSERT ON public.offline_device_observations TO licensing_runtime;
            GRANT EXECUTE ON FUNCTION public.due_scheduled_capacity_reductions(timestamptz) TO licensing_runtime;
            """);
        await merchant.Database.ExecuteSqlRawAsync(
            "GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO merchant_runtime;");
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

        // Verify the licensing migrator (source) is non-superuser NOBYPASSRLS where RLS hides observations,
        // and verify merchant Down under a non-superuser NOBYPASSRLS owner where RLS hides DeviceObservation rows.
        Assert.Equal("off", await source.Database.SqlQueryRaw<string>(
            "SELECT current_setting('is_superuser') AS \"Value\"").SingleAsync());
        Assert.False(await source.Database.SqlQueryRaw<bool>(
            "SELECT rolbypassrls AS \"Value\" FROM pg_roles WHERE rolname = current_user").SingleAsync());
        Assert.Equal(0L, await source.Database.SqlQueryRaw<long>(
            "SELECT count(*) AS \"Value\" FROM public.offline_device_observations").SingleAsync());
        await AssertRestrictedMerchantOwnerRefusedAsync(run.Fixture.MerchantDb);
    }

    private static async Task AssertRestrictedMerchantOwnerRefusedAsync(NpgsqlConnectionStringBuilder merchantOwnerConn)
    {
        var role = $"p0507_owner_{Guid.NewGuid():N}";
        var restrictedConn = new NpgsqlConnectionStringBuilder(merchantOwnerConn.ConnectionString)
            { Username = role, Password = role, Pooling = false };
        await LicensingPostgreSqlFixture.ExecuteAsync(merchantOwnerConn, $"""
            CREATE ROLE {role} LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD '{role}';
            GRANT CONNECT ON DATABASE "{merchantOwnerConn.Database}" TO {role};
            GRANT USAGE, CREATE ON SCHEMA public TO {role};
            ALTER TABLE public.entitlement_messages OWNER TO {role};
            ALTER TABLE public."__EFMigrationsHistory" OWNER TO {role};
            """);
        try
        {
            Assert.Equal(0L, await LicensingPostgreSqlFixture.ReadScalarAsync(restrictedConn,
                "SELECT count(*) FROM public.entitlement_messages"));
            await using var restricted = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(restrictedConn.ConnectionString).UseSnakeCaseNamingConvention().Options);
            var failure = await Record.ExceptionAsync(() => restricted.GetService<IMigrator>().MigrateAsync(MerchantPredecessor));
            Assert.Equal("55000", Assert.IsType<PostgresException>(failure?.GetBaseException()).SqlState);
            Assert.Equal(true, await LicensingPostgreSqlFixture.ReadScalarAsync(merchantOwnerConn,
                "SELECT relforcerowsecurity FROM pg_class WHERE relname = 'entitlement_messages' AND relnamespace = 'public'::regnamespace"));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await LicensingPostgreSqlFixture.ExecuteAsync(merchantOwnerConn, $"""
                ALTER TABLE public.entitlement_messages OWNER TO bootstrap;
                ALTER TABLE public."__EFMigrationsHistory" OWNER TO bootstrap;
                REVOKE ALL ON SCHEMA public FROM {role};
                REVOKE CONNECT ON DATABASE "{merchantOwnerConn.Database}" FROM {role};
                DROP ROLE IF EXISTS {role};
                """);
        }
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
