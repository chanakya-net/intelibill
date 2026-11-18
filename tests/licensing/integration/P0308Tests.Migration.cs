using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0308Tests
{
    [Fact]
    public async Task UpgradeRetainsPaidHistoryAndDowngradeRefusesRenewalEvidence()
    {
        const string previous = "20261117110000_ManualPaymentReceipts";
        await using var fixture = await StartAsync(database, March31, services: services => { P0310LegacyRouting.InstallLegacy(services); P0311LegacyIssuance.InstallLegacy(services); });
        var package = await fixture.PackageAsync(1);
        var fresh = await FirstQuoteAsync(package, "Month");
        var firstQuote = await AcceptAsync(package, await P0309Fixture.RepresentLegacyQuoteAsync(fixture.Hosts.LicensingOwner, fresh));
        var bought = await PayAsync(package, firstQuote);
        P0302Fixture.AssertStatus(bought, "Verified", "Fulfilled", "Applying");
        var first = bought.GetProperty("termId").GetGuid();
        await P0309Fixture.RepresentLegacyFulfilmentAsync(fixture.Hosts.LicensingOwner, firstQuote.OrderId);
        fixture.At(Utc("2027-04-20T06:00:00Z"));
        var pending = await AcceptAsync(package, await RenewalQuoteAsync(package, first, "Month"));
        var before = await TermRowAsync(fixture, first);

        // Populated #869 schema: down and up again keeps the paid term, its link and the accepted renewal quote.
        await using (var context = fixture.Hosts.Fixture.OpenMigrationContext())
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(previous);
            await migrator.MigrateAsync();
        }
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, P0302Fixture.RuntimeGrant);
        Assert.Equal(before, await TermRowAsync(fixture, first));
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments", "predecessor_term_id IS NULL"));

        var paid = await PayAsync(package, pending);
        P0302Fixture.AssertStatus(paid, "Verified", "Fulfilled", "Applying");
        await using (var context = fixture.Hosts.Fixture.OpenMigrationContext())
        {
            var refused = await Assert.ThrowsAnyAsync<Exception>(() => context.GetService<IMigrator>().MigrateAsync(previous));
            Assert.Equal("55000", Assert.IsType<PostgresException>(refused as PostgresException ?? refused.InnerException).SqlState);
        }
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments", $"predecessor_term_id = '{first:D}'"));
        foreach (var statement in new[]
        {
            "UPDATE public.payment_fulfilments SET grace_days_included = 0",
            "DELETE FROM public.payment_fulfilments WHERE predecessor_term_id IS NOT NULL",
        })
            Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() =>
                LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, statement))).SqlState);
    }
}
