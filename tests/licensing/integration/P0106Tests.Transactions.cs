using System.Net;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0106Tests
{
    [Fact]
    public async Task WaitingForBootstrapRowCannotExtendProofLifetime()
    {
        var runtime = await NewFixtureAsync();
        var clock = new TestClock();
        await using var api = new LicensingApiFactory(runtime, clock: clock);
        using var browser = Client(api);
        var proof = await PrepareAsync(runtime, clock);
        var csrf = await ReadCsrfAsync(browser);
        await using var connection = new NpgsqlConnection(runtime.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var hold = new NpgsqlCommand("SELECT id FROM initial_admin_bootstrap WHERE id = 1 FOR UPDATE",
            connection, transaction);
        await hold.ExecuteScalarAsync();

        var request = SubmitAsync(browser, csrf, proof, NewPassword());
        try
        {
            var owner = database.Bootstrap(runtime.Database);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (await ScalarAsync(owner, """
                SELECT EXISTS (SELECT FROM pg_stat_activity WHERE datname = current_database()
                  AND usename = 'licensing_runtime' AND wait_event_type = 'Lock'
                  AND query LIKE '%initial_admin_bootstrap%FOR UPDATE%')
                """) is not true)
                await Task.Delay(20, deadline.Token);

            clock.Advance(TimeSpan.FromMinutes(30));
        }
        finally
        {
            await transaction.RollbackAsync();
        }

        using var response = await request;
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertOwnerFactsAsync(runtime, proof, 0);
    }

    [Fact]
    public async Task FailedAuditInsertRollsBackIdentityCredentialAndProofConsumption()
    {
        var runtime = await NewFixtureAsync();
        var clock = new TestClock();
        await using var api = new LicensingApiFactory(runtime, clock: clock);
        using var browser = Client(api);
        var proof = await PrepareAsync(runtime, clock);
        var csrf = await ReadCsrfAsync(browser);
        var migrator = database.Login(runtime.Database!, LicensingPostgreSqlFixture.MigratorRole);
        await LicensingPostgreSqlFixture.ExecuteAsync(migrator,
            "ALTER TABLE provider_audit_events ADD CONSTRAINT reject_owner_audit CHECK (action <> 3)");
        var password = NewPassword();
        using (var failed = await SubmitAsync(browser, csrf, proof, password))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            Assert.False(failed.Headers.Contains("Set-Cookie"));
        }
        await AssertOwnerFactsAsync(runtime, proof, 0);
        Assert.Equal(0L, await ScalarAsync(runtime, "SELECT count(*) FROM bootstrap_enrollment_credentials"));
        Assert.Equal(1, await ScalarAsync(runtime, "SELECT attempts FROM bootstrap_rate_buckets WHERE id = 2"));

        await LicensingPostgreSqlFixture.ExecuteAsync(migrator,
            "ALTER TABLE provider_audit_events DROP CONSTRAINT reject_owner_audit");
        using var retried = await SubmitAsync(browser, csrf, proof, password);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        await AssertOwnerFactsAsync(runtime, proof, 1);
    }

    [Fact]
    public async Task PreparationCanReplaceUnusedProofButCannotSeedFromStartup()
    {
        var runtime = await NewFixtureAsync();
        var clock = new TestClock();
        await using var api = new LicensingApiFactory(runtime, clock: clock, environment: "Production");
        using var browser = Client(api);
        var retired = await PrepareAsync(runtime, clock);
        var active = await PrepareAsync(runtime, clock);
        Assert.Equal(1L, await ScalarAsync(runtime, "SELECT count(*) FROM provider_audit_events WHERE action = 2"));
        var csrf = await ReadCsrfAsync(browser);

        using var retiredAttempt = await SubmitAsync(browser, csrf, retired, NewPassword());
        Assert.Equal(HttpStatusCode.BadRequest, retiredAttempt.StatusCode);
        await AssertOwnerFactsAsync(runtime, active, 0);

        using var currentAttempt = await SubmitAsync(browser, csrf, active, NewPassword());
        Assert.Equal(HttpStatusCode.OK, currentAttempt.StatusCode);
        await AssertOwnerFactsAsync(runtime, active, 1);
    }
}
