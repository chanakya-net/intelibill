using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0212Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0212Tests
{
    private const string TrialMigration = "20261116160000_AddShopTrials";

    [Fact]
    public async Task GrantEvidenceIsScopedRetainedAndReservedByTheDatabase()
    {
        await using var fixture = await StartAsync(database);
        var (manager, _) = await fixture.ManagerAsync();
        using var session = manager;
        var (account, payer, shops) = await fixture.SeedAccountAsync(3);
        var grant = await GrantAsync(manager, account, Draft(shops[..2]));
        var grantId = grant.GetProperty("grantId").GetGuid();
        var term = grant.GetProperty("termId").GetGuid();
        var runtime = fixture.Provider.Runtime;

        // Runtime scope: nothing unscoped; an account read sees operational rows but no receipt or reason;
        // a named shop sees only its own link; an unnamed sibling sees nothing.
        Assert.Equal(0L, await ScopedCountAsync(runtime, "", "SELECT count(*) FROM complimentary_grants"));
        Assert.Equal(1L, await ScopedCountAsync(runtime, Account(account), "SELECT count(*) FROM complimentary_grants"));
        Assert.Equal(0L, await ScopedCountAsync(runtime, Account(account), "SELECT count(*) FROM complimentary_grant_receipts"));
        Assert.Equal(1L, await ScopedCountAsync(runtime, Shop(shops[0]), "SELECT count(*) FROM complimentary_grant_shops"));
        Assert.Equal(1L, await ScopedCountAsync(runtime, Shop(shops[0]), "SELECT count(*) FROM subscription_coverage_terms"));
        Assert.Equal(0L, await ScopedCountAsync(runtime, Shop(shops[0]), "SELECT count(*) FROM complimentary_grant_receipts"));
        Assert.Equal(0L, await ScopedCountAsync(runtime, Shop(shops[2]), "SELECT count(*) FROM complimentary_grants"));
        Assert.Equal(0L, await ScopedCountAsync(runtime, Shop(shops[2]), "SELECT count(*) FROM subscription_coverage_terms"));

        // Account context alone cannot forge a grant, a coverage term or a revision.
        await AssertRefusedAsync(runtime, Account(account) + $"""
            INSERT INTO complimentary_grant_scopes (account_id, revision) VALUES ('{account:D}', 5)
            """, "42501");
        await AssertRefusedAsync(runtime, Account(account) + $"""
            INSERT INTO subscription_coverage_terms (term_id, account_id, mode, capacity, starts_at, ends_at)
            VALUES (gen_random_uuid(), '{account:D}', 'Independent', 1, now(), now() + interval '1 day')
            """, "42501");

        // Retained: no rewrite, deletion, truncation or revision jump, even for the owner.
        await AssertRefusedAsync(fixture.Owner, $"UPDATE complimentary_grants SET ends_at = ends_at + interval '1 day' WHERE grant_id = '{grantId:D}'", "55000");
        await AssertRefusedAsync(fixture.Owner, "DELETE FROM complimentary_grant_shops", "55000");
        await AssertRefusedAsync(fixture.Owner, "UPDATE complimentary_grant_receipts SET reason = 'Rewritten'", "55000");
        await AssertRefusedAsync(fixture.Owner, "TRUNCATE complimentary_grants CASCADE", "55000");
        await AssertRefusedAsync(fixture.Owner, "DELETE FROM complimentary_grant_scopes", "55000");
        await AssertRefusedAsync(fixture.Owner, "UPDATE complimentary_grant_scopes SET revision = revision + 2", "55000");
        // The grant's term cannot be attached to an unnamed shop; a named shop cannot gain an overlapping window.
        var stranger = await Assert.ThrowsAsync<PostgresException>(() => P0209Fixture.AffiliateAsync(fixture.Owner, account, payer,
            Guid.NewGuid(), Guid.NewGuid(), term, Starts, Ends));
        Assert.Equal("23514", stranger.SqlState);
        await AssertRefusedAsync(fixture.Owner, $"""
            INSERT INTO complimentary_grant_shops (grant_id, account_id, term_id, shop_id, affiliation_request_id, starts_at, ends_at)
            SELECT grant_id, account_id, term_id, '{shops[2]:D}', (SELECT request_id FROM shop_affiliations WHERE shop_id = '{shops[1]:D}'),
                starts_at, ends_at FROM complimentary_grants WHERE grant_id = '{grantId:D}'
            """, "23514");
        Assert.Equal(1L, await fixture.CountAsync("SELECT count(*) FROM complimentary_grants"));
        Assert.Equal(2L, await fixture.CountAsync("SELECT count(*) FROM complimentary_grant_shops"));
    }

    [Fact]
    public async Task MigrationUpgradesRetainsFactsAndRefusesRollbackOnceGrantsExist()
    {
        await using var fixture = await StartAsync(database, publishEntitlements: false);
        var (manager, _) = await fixture.ManagerAsync();
        using var session = manager;
        var (account, payer, shops) = await fixture.SeedAccountAsync(1);
        await fixture.AffiliatedShopAsync(account, payer, new P0209Fixture.TermSeed(P0209Fixture.Utc("2027-02-01T04:30:00Z"),
            PaidEnds, AnchorLocal: "2027-02-01 10:00:00"));
        var before = await FactsSnapshotAsync(fixture);
        using var migrations = new P0201Fixture(database, fixture.Provider.Runtime.Database!);

        // Without grants the migration can be stepped down and up again with the paid facts intact.
        await using (var context = migrations.OpenMigrationContext())
        {
            await context.GetService<IMigrator>().MigrateAsync(TrialMigration);
            Assert.Equal(0L, await fixture.CountAsync(
                "SELECT count(*) FROM information_schema.tables WHERE table_name LIKE 'complimentary_grant%'"));
            await context.Database.MigrateAsync();
        }
        // The deployed role script grants the runtime identity on tables the migrator creates (default privileges).
        await fixture.Provider.ExecuteAsync("GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO licensing_runtime");
        Assert.Equal(before, await FactsSnapshotAsync(fixture));

        await GrantAsync(manager, account, Draft(shops));
        await using (var context = migrations.OpenMigrationContext())
        {
            var refused = await Assert.ThrowsAsync<InvalidOperationException>(
                () => context.GetService<IMigrator>().MigrateAsync(TrialMigration));
            Assert.Equal("55000", Assert.IsType<PostgresException>(refused.InnerException).SqlState);
        }
        Assert.Equal(1L, await fixture.CountAsync("SELECT count(*) FROM complimentary_grants"));
        // The runtime identity cannot change the schema.
        await AssertRefusedAsync(fixture.Provider.Runtime, "DROP TABLE complimentary_grants", "42501");
    }

    private static string Account(Guid account) =>
        $"SELECT set_config('app.billing_account_id', '{account:D}', true);";

    private static string Shop(Guid shop) => $"SELECT set_config('app.status_shop_id', '{shop:D}', true);";

    private static async Task<long> ScopedCountAsync(NpgsqlConnectionStringBuilder runtime, string scope, string sql)
    {
        await using var connection = new NpgsqlConnection(runtime.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        if (scope.Length > 0)
            await using (var set = new NpgsqlCommand(scope, connection, transaction))
                await set.ExecuteNonQueryAsync();
        await using var count = new NpgsqlCommand(sql, connection, transaction);
        return (long)(await count.ExecuteScalarAsync())!;
    }

    private static async Task AssertRefusedAsync(NpgsqlConnectionStringBuilder settings, string sql, string state)
    {
        await using var connection = new NpgsqlConnection(settings.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        var refused = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.True(state == refused.SqlState, $"{sql}: {refused.SqlState}");
    }
}
