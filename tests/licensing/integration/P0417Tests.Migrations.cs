using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0417Tests;

namespace Intelibill.Licensing.Integration.Tests;

// Independent merchant database so earlier cases cannot supply downgrade-blocking evidence.
public sealed class P0417TestsMigrations(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task UpgradeRetainsClosuresAndRefusesLossyDowngrade()
    {
        var clock = new P0201Clock { Now = P0202Fixture.WholeSecondNow() };
        await using var fixture = await P0213Fixture.StartAsync(database, businessClock: clock);
        var (owner, _, route) = await CloseAsync(fixture, clock.Now);
        await fixture.DrainAsync(source: true);
        var closed = await fixture.LatestSnapshotAsync(owner.ShopId);
        await using var source = fixture.Hosts.Fixture.OpenMigrationContext();
        await using var merchant = MerchantDb(database);
        Assert.False(source.Database.HasPendingModelChanges());
        Assert.False(merchant.Database.HasPendingModelChanges());
        const string sourcePredecessor = "20261121103000_ClosureNoticeOperations";
        const string merchantPredecessor = "20261121100000_MerchantClosureConsents";
        await source.GetService<IMigrator>().MigrateAsync(sourcePredecessor);
        await merchant.GetService<IMigrator>().MigrateAsync(merchantPredecessor);
        await source.Database.MigrateAsync();
        await merchant.Database.MigrateAsync();
        await source.Database.ExecuteSqlRawAsync(P0302Fixture.RuntimeGrant);
        await P0213Fixture.EnableReopeningAsync(fixture.Hosts.LicensingOwner);
        Assert.Equal(closed, await fixture.LatestSnapshotAsync(owner.ShopId));
        Assert.Equal("Closed", await fixture.MerchantScalarAsync($"SELECT state FROM shop_license_states WHERE shop_id = '{owner.ShopId:D}'"));

        using var admin = await SignInAsync(fixture);
        using var reopened = await P0212Fixture.PostAsync(admin, route, Command(await PreviewAsync(admin, route), owner));
        Assert.True(reopened.StatusCode == HttpStatusCode.Created, await reopened.Content.ReadAsStringAsync());
        await fixture.DrainAsync(source: true);
        await AssertRetentionAsync(fixture);
        await AssertDowngradeRefusedAsync(source, sourcePredecessor);
        await AssertDowngradeRefusedAsync(merchant, merchantPredecessor);
    }

    private static async Task AssertRetentionAsync(P0213Fixture fixture)
    {
        Assert.Equal(true, await fixture.SourceScalarAsync("""
            SELECT relforcerowsecurity FROM pg_class WHERE relname = 'operational_closure_reopenings'
            """));
        Assert.Equal(0L, await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.Fixture.Runtime,
            "SELECT count(*) FROM public.operational_closure_reopenings"));
        Assert.Equal(0L, await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.Fixture.Runtime,
            "SELECT count(*) FROM public.active_operational_closure_targets"));
        Assert.Equal(1L, await fixture.Trial.CountAsync("operational_closure_targets"));
    }

    private static async Task AssertDowngradeRefusedAsync(DbContext context, string predecessor)
    {
        var failure = await Record.ExceptionAsync(() => context.GetService<IMigrator>().MigrateAsync(predecessor));
        Assert.Equal("55000", Assert.IsType<PostgresException>(failure?.GetBaseException()).SqlState);
    }
}
