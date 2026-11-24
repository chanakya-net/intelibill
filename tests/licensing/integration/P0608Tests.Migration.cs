using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0608Tests
{
    private const string PreviousMigration = "20261124110000_CapacityChangesRefundsAndDocuments";

    [Fact]
    public async Task RefundRequestMigrationRetainsRequestsAndPaymentEvidence()
    {
        await using var fixture = await StartAsync(database, RequestedInstant);
        var package = await fixture.PackageAsync(1, ["Migration Shop"]);
        var (purchase, term) = await PurchaseAsync(package, "Month");
        await DrainAsync(fixture);
        var confirmation = await ConfirmationIdAsync(package.Payer.Client, package.Account, purchase.OrderId);
        var before = await SnapshotAsync(fixture, package, term);

        await using (var context = fixture.Hosts.Fixture.OpenMigrationContext())
        {
            Assert.False(context.Database.HasPendingModelChanges());
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            Assert.True(await fixture.ScalarAsync("SELECT to_regclass('public.refund_requests')::text") is null or DBNull);
            await migrator.MigrateAsync();
            Assert.Equal(true, await fixture.ScalarAsync(
                "SELECT relforcerowsecurity FROM pg_class WHERE relname = 'refund_requests' AND relnamespace = 'public'::regnamespace"));
        }

        // Runtime grants are environment-owned: a recreated table needs SELECT, INSERT again.
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, P0302Fixture.RuntimeGrant +
            ";GRANT SELECT, INSERT ON public.refund_requests TO licensing_runtime;");
        Assert.Equal(before, await SnapshotAsync(fixture, package, term));

        using var created = await PostRefundAsync(package.Payer.Client, package.Account,
            RequestBody(purchase.OrderId, confirmation, 3500L, "Retained refund request"));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        await using (var context = fixture.Hosts.Fixture.OpenMigrationContext())
        {
            Assert.Equal("off", await context.Database.SqlQueryRaw<string>(
                "SELECT current_setting('is_superuser') AS \"Value\"").SingleAsync());
            var refused = await Record.ExceptionAsync(() => context.GetService<IMigrator>().MigrateAsync(PreviousMigration));
            Assert.Equal("55000", Assert.IsType<PostgresException>(refused?.GetBaseException()).SqlState);
        }

        foreach (var statement in new[]
        {
            "UPDATE public.refund_requests SET amount_paise = 1",
            "DELETE FROM public.refund_requests",
            "TRUNCATE public.refund_requests CASCADE",
        })
        {
            var refused = await Assert.ThrowsAsync<PostgresException>(() =>
                LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, statement));
            Assert.Equal("55000", refused.SqlState);
        }

        Assert.Equal(1L, await fixture.CountAsync("refund_requests", $"confirmation_id = '{confirmation:D}' AND amount_paise = 3500"));
        Assert.Equal(before, await SnapshotAsync(fixture, package, term));
    }
}
