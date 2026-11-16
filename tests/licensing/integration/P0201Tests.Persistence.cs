using System.Net;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0201Tests
{
    [Fact]
    public async Task TwoReplicas_ConsumeOneReplayIdOnce()
    {
        using var fixture = await P0201Fixture.CreateAsync(database);
        var account = Guid.NewGuid();
        var actor = Guid.NewGuid();
        await fixture.GrantAsync(account, actor);
        var proof = CreateAssertion(fixture, account, actor);
        var gate = new ReplicaGate();
        await using var first = fixture.LicensingApi(configure: services => services.AddSingleton(gate));
        await using var second = fixture.LicensingApi(configure: services => services.AddSingleton(gate));
        using var a = first.CreateClient();
        using var b = second.CreateClient();
        var responses = await Task.WhenAll(
            P0201Fixture.SendAssertionAsync(a, account, proof.Request, proof.Token),
            P0201Fixture.SendAssertionAsync(b, account, proof.Request, proof.Token));
        using var one = responses[0];
        using var two = responses[1];
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        await AssertProblemAsync(Assert.Single(responses, response => response.StatusCode != HttpStatusCode.OK),
            HttpStatusCode.Unauthorized, "Billing.DelegationReplayed");
        Assert.Equal(1L, await ReplayCountAsync(fixture));

        await first.DisposeAsync();
        await second.DisposeAsync();
        await using var restarted = fixture.LicensingApi();
        using var client = restarted.CreateClient();
        using var replay = await P0201Fixture.SendAssertionAsync(client, account, proof.Request, proof.Token);
        await AssertProblemAsync(replay, HttpStatusCode.Unauthorized, "Billing.DelegationReplayed");
    }

    [Fact]
    public async Task RevokedMembership_IsDeniedBeforeReplay()
    {
        using var fixture = await P0201Fixture.CreateAsync(database);
        var account = Guid.NewGuid();
        var actor = Guid.NewGuid();
        await fixture.GrantAsync(account, actor);
        var first = CreateAssertion(fixture, account, actor);
        var unused = CreateAssertion(fixture, account, actor);
        await using var licensing = fixture.LicensingApi();
        using var client = licensing.CreateClient();

        using var accepted = await P0201Fixture.SendAssertionAsync(client, account, first.Request, first.Token);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        await fixture.RevokeAsync(account, actor);
        foreach (var proof in new[] { first, unused })
        {
            using var denied = await P0201Fixture.SendAssertionAsync(client, account, proof.Request, proof.Token);
            await AssertProblemAsync(denied, HttpStatusCode.Forbidden, "Billing.AccountAccessDenied");
        }
        Assert.Equal(1L, await ReplayCountAsync(fixture));
    }

    [Fact]
    public async Task PooledScope_DoesNotLeakAccounts()
    {
        using var fixture = await P0201Fixture.CreateAsync(database);
        var account = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var other = Guid.NewGuid();
        await fixture.GrantAsync(account, actor);
        await fixture.GrantAsync(other, actor);
        await using var licensing = fixture.LicensingApi();
        using var client = licensing.CreateClient();
        var dataSource = licensing.Services.GetRequiredService<NpgsqlDataSource>();
        var pid = (int)(await ReadPoolAsync(dataSource, "SELECT pg_backend_pid()"))!;

        foreach (var current in new[] { account, other })
        {
            var proof = CreateAssertion(fixture, current, actor);
            using var accepted = await P0201Fixture.SendAssertionAsync(client, current, proof.Request, proof.Token);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            using var noMember = await P0201Fixture.SendAssertionAsync(client, Guid.NewGuid(), proof.Request, proof.Token);
            Assert.Equal(HttpStatusCode.Unauthorized, noMember.StatusCode);
            Assert.Equal(0L, await ReadPoolAsync(dataSource, "SELECT count(*) FROM merchant_billing_delegation_uses"));
        }
        var denied = CreateAssertion(fixture, account, Guid.NewGuid());
        using var rolledBack = await P0201Fixture.SendAssertionAsync(client, account, denied.Request, denied.Token);
        await AssertProblemAsync(rolledBack, HttpStatusCode.Forbidden, "Billing.AccountAccessDenied");
        using var anonymous = await client.GetAsync(P0201Fixture.InternalPath(account));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(pid, await ReadPoolAsync(dataSource, "SELECT pg_backend_pid()"));
        Assert.Equal(0L, await ReadPoolAsync(dataSource, "SELECT count(*) FROM merchant_billing_delegation_uses"));
        Assert.Equal(2L, await ReplayCountAsync(fixture));

        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        using var scope = new NpgsqlCommand("""
            SELECT set_config('app.billing_account_id', $1, true), set_config('app.billing_actor_id', $2, true);
            """, connection, transaction);
        scope.Parameters.AddWithValue(account.ToString("D"));
        scope.Parameters.AddWithValue(actor.ToString("D"));
        await scope.ExecuteNonQueryAsync();
        await using var read = new NpgsqlCommand(
            "SELECT account_id FROM merchant_billing_delegation_uses", connection, transaction);
        Assert.Equal(account, await read.ExecuteScalarAsync());
        read.CommandText = "SELECT count(*) FROM merchant_billing_delegation_uses";
        Assert.Equal(1L, await read.ExecuteScalarAsync());
        read.CommandText = "DELETE FROM merchant_billing_delegation_uses";
        var immutable = await Assert.ThrowsAsync<PostgresException>(async () => await read.ExecuteNonQueryAsync());
        Assert.Equal("55000", immutable.SqlState);
    }

    [Fact]
    public async Task ReplayMigration_UpgradesAndKeepsRuntimeIsolated()
    {
        using var fixture = await P0201Fixture.CreateAsync(database);
        await using var db = fixture.OpenMigrationContext();
        var predecessor = "20261115010116_AddAdminCapabilities";
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(predecessor);
        Assert.Equal(true, await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Owner,
            "SELECT to_regclass('public.merchant_billing_delegation_uses') IS NULL"));
        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(true, await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Runtime, """
            SELECT relrowsecurity AND relforcerowsecurity
            FROM pg_class WHERE oid = 'public.merchant_billing_delegation_uses'::regclass
            """));
        Assert.Equal(false, await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Runtime,
            "SELECT rolbypassrls OR rolsuper FROM pg_roles WHERE rolname = current_user"));

        var wrongDatabase = fixture.Runtime;
        wrongDatabase.Database = LicensingPostgreSqlFixture.MerchantDatabase;
        await using var forbidden = new NpgsqlConnection(wrongDatabase.ConnectionString);
        var error = await Assert.ThrowsAsync<PostgresException>(async () => await forbidden.OpenAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    private async Task<object?> ReplayCountAsync(P0201Fixture fixture) =>
        await LicensingPostgreSqlFixture.ReadScalarAsync(database.Bootstrap(fixture.Owner.Database),
            "SELECT count(*) FROM merchant_billing_delegation_uses");

    private static async Task<object?> ReadPoolAsync(NpgsqlDataSource dataSource, string sql)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    internal sealed class ReplicaGate
    {
        private readonly TaskCompletionSource _both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        internal Task ArriveAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrivals) == 2)
                _both.TrySetResult();
            return _both.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }
}
