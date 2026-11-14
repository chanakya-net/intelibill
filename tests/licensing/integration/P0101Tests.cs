using System.Net;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit.Abstractions;

namespace Intelibill.Licensing.Integration.Tests;

[Collection(LicensingDatabaseTests.Name)]

public sealed partial class P0101Tests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task FreshLicensingDatabase_ReportsOwnReadiness()
    {
        var before = await database.ReadLicensingCatalogAsync();
        await using var api = CreateLicensingApi();
        using var client = api.CreateClient();

        await AssertProbeAsync(client, "ready", HttpStatusCode.OK, "ready");
        await AssertProbeAsync(client, "live", HttpStatusCode.OK, "live");

        await using var scope = api.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LicensingDbContext>();
        Assert.Empty(context.Model.GetEntityTypes());
        await context.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await using var command = new NpgsqlCommand("SELECT current_database() || ':' || current_user", connection);
        var identity = await command.ExecuteScalarAsync();
        Assert.Equal("licensing:licensing_runtime", identity);
        Assert.Equal(string.Empty, before.Tables);
        Assert.Equal(before, await database.ReadLicensingCatalogAsync());
        output.WriteLine($"Fresh database identity: {identity}; empty catalog of user relations preserved.");
    }

    [Fact]
    public async Task DatabaseOutage_RecoveryAndConcurrentStartup_AreReadOnly()
    {
        await database.CreateProbeTableAsync();
        try
        {
            var ddlBefore = await database.ReadRuntimeDdlAsync();
            await VerifyOutageRecoveryAsync();
            await VerifyConcurrentStartupAsync();
            Assert.Equal(ddlBefore, await database.ReadRuntimeDdlAsync());
            output.WriteLine("PostgreSQL logged no new runtime DDL during outage, recovery or concurrent startup.");
        }
        finally
        {
            await database.DropProbeTableAsync();
        }
    }

    private async Task VerifyOutageRecoveryAsync()
    {
        var before = await database.ReadLicensingCatalogAsync();
        await database.SetConnectionsAsync(LicensingPostgreSqlFixture.LicensingDatabase, allowed: false);
        await using var api = CreateLicensingApi();
        try
        {
            using var unavailable = api.CreateClient();
            await AssertProbeAsync(unavailable, "live", HttpStatusCode.OK, "live");
            await AssertProbeAsync(unavailable, "ready", HttpStatusCode.ServiceUnavailable, "unavailable");
        }
        finally
        {
            await database.SetConnectionsAsync(LicensingPostgreSqlFixture.LicensingDatabase, allowed: true);
        }

        using var client = api.CreateClient();
        await AssertProbeAsync(client, "ready", HttpStatusCode.OK, "ready");
        Assert.Equal(before, await database.ReadLicensingCatalogAsync());
    }

    private async Task VerifyConcurrentStartupAsync()
    {
        var before = await database.ReadLicensingCatalogAsync();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = StartAndProbeAsync(start.Task);
        var second = StartAndProbeAsync(start.Task);
        start.SetResult();
        await Task.WhenAll(first, second);

        var after = await database.ReadLicensingCatalogAsync();
        Assert.Equal(before, after);
        Assert.Contains("public.licensing_probe", after.Tables, StringComparison.Ordinal);
        Assert.DoesNotContain("__EFMigrationsHistory", after.Tables, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("__ef_migrations_history", after.Tables, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(42, after.ProbeValue);
        output.WriteLine("Concurrent startup and restart preserved user schemas, relations and the probe row (42); no migration history.");
    }

    private async Task StartAndProbeAsync(Task start)
    {
        await start;
        await using var api = CreateLicensingApi();
        using var client = api.CreateClient();
        await AssertProbeAsync(client, "ready", HttpStatusCode.OK, "ready");
    }

    [Fact]
    public async Task RuntimeAndMigrator_AreRestrictedAndDatabaseIsolated()
    {
        await AssertLoginDeniedAsync(database.Login(
            LicensingPostgreSqlFixture.MerchantDatabase, LicensingPostgreSqlFixture.RuntimeRole));
        await AssertLoginDeniedAsync(database.Login(
            LicensingPostgreSqlFixture.LicensingDatabase, LicensingPostgreSqlFixture.MerchantRole));
        await database.CreateProbeTableAsync();
        try
        {
            var runtime = database.Login(LicensingPostgreSqlFixture.LicensingDatabase, LicensingPostgreSqlFixture.RuntimeRole);
            var migrator = database.Login(LicensingPostgreSqlFixture.LicensingDatabase, LicensingPostgreSqlFixture.MigratorRole);
            await using (var api = new LicensingApiFactory(runtime, migrator))
            {
                using var client = api.CreateClient();
                await AssertProbeAsync(client, "ready", HttpStatusCode.OK, "ready");
                await using var scope = api.Services.CreateAsyncScope();
                var connectionString = scope.ServiceProvider.GetRequiredService<LicensingDbContext>().Database.GetConnectionString();
                Assert.Equal(LicensingPostgreSqlFixture.RuntimeRole, new NpgsqlConnectionStringBuilder(connectionString).Username);
                await LicensingPostgreSqlFixture.ExecuteAsync(runtime, "INSERT INTO public.licensing_probe VALUES (43)");
            }

            var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                LicensingPostgreSqlFixture.ExecuteAsync(runtime, "CREATE TABLE public.runtime_ddl_denied (id int)"));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
            await using var restarted = CreateLicensingApi();
            using var restartedClient = restarted.CreateClient();
            await AssertProbeAsync(restartedClient, "ready", HttpStatusCode.OK, "ready");
            var value = await LicensingPostgreSqlFixture.ReadScalarAsync(runtime,
                "SELECT value FROM public.licensing_probe WHERE value = 43");
            Assert.Equal(43, value);
            output.WriteLine("Both crossed database logins and runtime CREATE TABLE received SQLSTATE 42501; migrator DDL and runtime DML succeeded; row 43 retained after restart.");
        }
        finally
        {
            await database.DropProbeTableAsync();
        }
    }

    [Theory]
    [InlineData("ALTER ROLE licensing_runtime BYPASSRLS", "ALTER ROLE licensing_runtime NOBYPASSRLS", "Testing")]
    [InlineData("ALTER ROLE licensing_runtime REPLICATION", "ALTER ROLE licensing_runtime NOREPLICATION", "Testing")]
    [InlineData("GRANT pg_read_all_data TO licensing_runtime WITH INHERIT TRUE, SET FALSE", "REVOKE pg_read_all_data FROM licensing_runtime", "Testing")]
    [InlineData("ALTER ROLE licensing_runtime CREATEDB", "ALTER ROLE licensing_runtime NOCREATEDB", "Testing")]
    [InlineData("ALTER ROLE licensing_runtime CREATEROLE", "ALTER ROLE licensing_runtime NOCREATEROLE", "Testing")]
    [InlineData("GRANT TEMP ON DATABASE licensing TO licensing_runtime", "REVOKE TEMP ON DATABASE licensing FROM licensing_runtime", "Testing")]
    [InlineData("GRANT CREATE ON SCHEMA public TO licensing_runtime", "REVOKE CREATE ON SCHEMA public FROM licensing_runtime", "Development")]
    [InlineData("CREATE TABLE public.runtime_owned (id int); ALTER TABLE public.runtime_owned OWNER TO licensing_runtime", "DROP TABLE public.runtime_owned", "Testing")]
    [InlineData("GRANT licensing_migrator TO licensing_runtime WITH INHERIT FALSE, SET TRUE", "REVOKE licensing_migrator FROM licensing_runtime", "Testing")]
    public async Task UnsafeRuntimePrivileges_ReturnUnavailable(string grant, string revoke, string environment)
    {
        await using var api = new LicensingApiFactory(database.Login(
            LicensingPostgreSqlFixture.LicensingDatabase, LicensingPostgreSqlFixture.RuntimeRole), environment: environment);
        using var client = api.CreateClient();
        await AssertProbeAsync(client, "ready", HttpStatusCode.OK, "ready");
        await database.ChangeRuntimeRoleAsync(grant);
        try
        {
            await AssertProbeAsync(client, "ready", HttpStatusCode.ServiceUnavailable, "unavailable");
        }
        finally
        {
            await database.ChangeRuntimeRoleAsync(revoke);
        }

        await AssertProbeAsync(client, "ready", HttpStatusCode.OK, "ready");
    }

    [Theory]
    [InlineData("bootstrap", "Development")]
    [InlineData(LicensingPostgreSqlFixture.MigratorRole, "Testing")]
    public async Task PrivilegedLogin_ReturnsUnavailable(string role, string environment)
    {
        await using var api = new LicensingApiFactory(database.Login(
            LicensingPostgreSqlFixture.LicensingDatabase, role), environment: environment);
        using var client = api.CreateClient();
        await AssertProbeAsync(client, "live", HttpStatusCode.OK, "live");
        await AssertProbeAsync(client, "ready", HttpStatusCode.ServiceUnavailable, "unavailable");
    }

    private static async Task AssertLoginDeniedAsync(NpgsqlConnectionStringBuilder settings)
    {
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            LicensingPostgreSqlFixture.ExecuteAsync(settings, "SELECT 1"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
    }

    private LicensingApiFactory CreateLicensingApi() => new(
        database.Login(LicensingPostgreSqlFixture.LicensingDatabase, LicensingPostgreSqlFixture.RuntimeRole));

    private async Task AssertProbeAsync(HttpClient client, string probe, HttpStatusCode expected, string status)
    {
        using var response = await client.GetAsync(new Uri("/health/" + probe, UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"Licensing {probe}: {(int)response.StatusCode} {body}");
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal($$"""{"service":"intelibill-licensing","status":"{{status}}"}""", body);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }
}
