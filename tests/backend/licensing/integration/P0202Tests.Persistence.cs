using System.Net;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0202Tests
{
    [Fact]
    public async Task BillingSchema_ForcesRowSecurityAndAcceptsRuntimeWrites()
    {
        using var fixture = await P0201Fixture.CreateAsync(database);
        var superuser = database.Bootstrap(fixture.Owner.Database);
        await using var db = fixture.OpenMigrationContext();
        Assert.False(db.Database.HasPendingModelChanges());
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
        Assert.Single(await P0202Fixture.ReadAccountRowsAsync(database, fixture));
    }
}
