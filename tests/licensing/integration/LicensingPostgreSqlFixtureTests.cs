namespace Intelibill.Licensing.Integration.Tests;

public sealed class LicensingPostgreSqlFixtureTests(LicensingPostgreSqlFixture database)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task MigratedDatabaseCopiesStartCleanAndKeepTheTemplateClosed()
    {
        var fixtures = new P0108Tests(database);
        var first = await fixtures.SignInDatabaseAsync();
        var firstOwner = database.Login(first.Database!, LicensingPostgreSqlFixture.MigratorRole);
        await LicensingPostgreSqlFixture.ExecuteAsync(firstOwner, "CREATE TABLE public.fixture_probe (value integer)");

        var second = await fixtures.SignInDatabaseAsync();

        Assert.NotEqual(first.Database, second.Database);
        Assert.Equal(0L, await LicensingPostgreSqlFixture.ReadScalarAsync(second, "SELECT count(*) FROM admin_identities"));
        Assert.Equal(true, await LicensingPostgreSqlFixture.ReadScalarAsync(second,
            "SELECT to_regclass('public.fixture_probe') IS NULL"));
        Assert.Equal(true, await LicensingPostgreSqlFixture.ReadScalarAsync(second, """
            SELECT pg_get_userbyid(datdba) = 'licensing_migrator'
                AND has_database_privilege('licensing_runtime', oid, 'CONNECT')
                AND NOT has_database_privilege('merchant_runtime', oid, 'CONNECT')
            FROM pg_database
            WHERE datname = current_database();
            """));
        Assert.Equal(1L, await LicensingPostgreSqlFixture.ReadScalarAsync(database.Bootstrap(), """
            SELECT count(*) FROM pg_database
            WHERE datname = 'licensing_template' AND datistemplate AND NOT datallowconn;
            """));
    }
}
