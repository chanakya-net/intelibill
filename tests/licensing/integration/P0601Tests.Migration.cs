using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0601Tests
{
    private const string PreviousMigration = "20261124100000_OfflineDeviceObservations";

    /// <summary>
    /// Over populated paid evidence the migration goes down and up without touching the purchased term, then a quote
    /// is retained: rows refuse update/delete and the downgrade refuses under the non-superuser migrator.
    /// </summary>
    [Fact]
    public async Task CapacityQuoteMigrationPreservesPurchasedEvidence()
    {
        await using var fixture = await StartAsync(database, January31);
        var package = await fixture.PackageAsync(2, ShopNames);
        var (_, term) = await PurchaseAsync(package, "Month");
        var before = await EvidenceAsync(fixture, term);

        await using (var context = fixture.Hosts.Fixture.OpenMigrationContext())
        {
            Assert.False(context.Database.HasPendingModelChanges());
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            Assert.True(await fixture.ScalarAsync("SELECT to_regclass('public.capacity_change_quotes')::text") is null or DBNull);
            await migrator.MigrateAsync();
            Assert.Equal(true, await fixture.ScalarAsync(
                "SELECT relforcerowsecurity FROM pg_class WHERE relname = 'capacity_change_quotes' AND relnamespace = 'public'::regnamespace"));
        }
        // Runtime grants are environment-owned: a recreated table needs them again, and only select/insert.
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, P0302Fixture.RuntimeGrant +
            ";GRANT SELECT, INSERT ON public.capacity_change_quotes, public.capacity_change_quote_shops TO licensing_runtime;");
        Assert.Equal(before, await EvidenceAsync(fixture, term));

        fixture.At(February14);
        var quote = await QuoteAsync(package, term, Request(package.Plan, 4, 3, February29));
        Assert.Equal("Payable", quote.GetProperty("outcome").GetString());
        await using (var context = fixture.Hosts.Fixture.OpenMigrationContext())
        {
            Assert.Equal("off", await context.Database.SqlQueryRaw<string>(
                "SELECT current_setting('is_superuser') AS \"Value\"").SingleAsync());
            var refused = await Record.ExceptionAsync(() => context.GetService<IMigrator>().MigrateAsync(PreviousMigration));
            Assert.Equal("55000", Assert.IsType<PostgresException>(refused?.GetBaseException()).SqlState);
        }
        foreach (var statement in new[]
        {
            "UPDATE public.capacity_change_quotes SET amount_paise = 1",
            "DELETE FROM public.capacity_change_quote_shops",
            "TRUNCATE public.capacity_change_quotes CASCADE",
        })
            Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() =>
                LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, statement))).SqlState);
        Assert.Equal(1L, await fixture.CountAsync("capacity_change_quotes", $"term_id = '{term:D}' AND amount_paise = 7759"));
        Assert.Equal(before, await EvidenceAsync(fixture, term));
    }
}
