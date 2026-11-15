using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0202Tests
{
    private const string Predecessor = "20261115124718_AddMerchantBillingDelegationUses";

    [Fact]
    public async Task BillingMigration_UpgradesAndRefusesEvidenceLoss()
    {
        using var fixture = await P0201Fixture.CreateAsync(database);
        var superuser = database.Bootstrap(fixture.Owner.Database);
        await using var db = fixture.OpenMigrationContext();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(Predecessor);
        Assert.Equal(true, await LicensingPostgreSqlFixture.ReadScalarAsync(superuser,
            "SELECT to_regclass('public.billing_accounts') IS NULL AND to_regclass('public.billing_account_members') IS NULL"));
        Assert.Equal("NO", await LicensingPostgreSqlFixture.ReadScalarAsync(superuser, """
            SELECT is_nullable FROM information_schema.columns
            WHERE table_name = 'merchant_billing_delegation_uses' AND column_name = 'account_id'
            """));
        Assert.Equal(0L, await LicensingPostgreSqlFixture.ReadScalarAsync(superuser, """
            SELECT count(*) FROM pg_policies
            WHERE tablename = 'merchant_billing_delegation_uses' AND coalesce(qual, with_check) LIKE '%DISTINCT%'
            """));

        var account = Guid.NewGuid();
        await LicensingPostgreSqlFixture.ExecuteAsync(superuser, $"""
            INSERT INTO merchant_billing_delegation_uses
                (issuer, replay_id, account_id, actor_id, action, request_id, key_id, issued_at, expires_at, consumed_at)
            VALUES ('InteliBill.Merchant.Billing', '{Guid.NewGuid():D}', '{account:D}', '{Guid.NewGuid():D}',
                'billing.account.read', '{Guid.NewGuid():D}', 'local-v1', now(), now() + interval '60 seconds', now())
            """);
        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(account, await LicensingPostgreSqlFixture.ReadScalarAsync(superuser,
            "SELECT account_id FROM merchant_billing_delegation_uses"));
        Assert.Equal(2L, await LicensingPostgreSqlFixture.ReadScalarAsync(superuser, """
            SELECT count(*) FROM pg_class
            WHERE relname IN ('billing_accounts', 'billing_account_members') AND relrowsecurity AND relforcerowsecurity
            """));
        Assert.Equal(false, await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Runtime,
            "SELECT rolbypassrls OR rolsuper FROM pg_roles WHERE rolname = current_user"));

        await using (var licensing = P0202Fixture.LicensingApi(fixture))
        {
            using var client = licensing.CreateClient();
            using var created = await P0202Fixture.CreateAsync(client, fixture, Guid.NewGuid(), Guid.NewGuid(), "Kept");
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }
        var refused = await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync(Predecessor));
        Assert.Equal("55000", Assert.IsType<PostgresException>(refused is PostgresException ? refused : refused.InnerException).SqlState);
        Assert.Single(await P0202Fixture.ReadAccountRowsAsync(database, fixture));
        Assert.Equal(true, await LicensingPostgreSqlFixture.ReadScalarAsync(superuser, """
            SELECT relforcerowsecurity FROM pg_class WHERE oid = 'public.billing_accounts'::regclass
            """));
    }
}
