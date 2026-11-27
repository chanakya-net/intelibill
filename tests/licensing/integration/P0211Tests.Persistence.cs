using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0211Tests
{
    [Fact]
    public async Task ExtensionEvidenceIsScopedRetainedAndOnlyAdvances()
    {
        await using var fixture = await P0210Fixture.StartAsync(database);
        var (owner, account) = await StartTrialAsync(fixture);
        await ApproveAsync(fixture, account, owner.ShopId);
        var runtime = fixture.Catalogue.Runtime;
        var accountScope = $"SELECT set_config('app.billing_account_id', '{account:D}', true);";
        var shopScope = $"SELECT set_config('app.status_shop_id', '{owner.ShopId:D}', true);";
        Assert.Equal(0L, await ScopeCountAsync(runtime, "", "trial_extensions"));
        Assert.Equal(1L, await ScopeCountAsync(runtime, accountScope, "trial_extensions"));
        Assert.Equal(0L, await ScopeCountAsync(runtime, accountScope, "trial_extension_receipts"));
        Assert.Equal(1L, await ScopeCountAsync(runtime, shopScope, "trial_extensions"));
        Assert.Equal(0L, await ScopeCountAsync(runtime, shopScope, "trial_extension_receipts"));
        Assert.Equal(0L, await ScopeCountAsync(runtime,
            $"SELECT set_config('app.status_shop_id', '{Guid.NewGuid():D}', true);", "trial_extensions"));

        await RefusedAsync(runtime, accountScope + InsertExtension(2, "ends_at", "ends_at + interval '1 day'"), "42501");
        foreach (var table in new[] { "trial_extensions", "trial_extension_receipts", "trial_history" })
        {
            await RefusedAsync(fixture.LicensingOwner, $"UPDATE {table} SET shop_id = shop_id", "55000");
            await RefusedAsync(fixture.LicensingOwner, $"DELETE FROM {table}", "55000");
            await RefusedAsync(fixture.LicensingOwner, $"TRUNCATE {table} CASCADE", "55000");
        }
        await RefusedAsync(fixture.LicensingOwner, InsertExtension(3, "ends_at", "ends_at + interval '1 day'"), "23514");
        await RefusedAsync(fixture.LicensingOwner, InsertExtension(2, "ends_at", "ends_at - interval '1 day'"), "23514");
        await RefusedAsync(fixture.LicensingOwner,
            InsertExtension(2, "ends_at", "ends_at + interval '1 day'") + "; SET CONSTRAINTS ALL IMMEDIATE;", "23514");
        Assert.Equal(1L, await fixture.CountAsync("trial_extensions"));
    }

    private static string InsertExtension(int revision, string previous, string next) => $"""
        INSERT INTO trial_extensions (extension_id, shop_id, account_id, term_id, revision, previous_ends_at,
            ends_at, recorded_at, policy_version)
        SELECT gen_random_uuid(), shop_id, account_id, term_id, {revision}, {previous}, {next}, recorded_at, policy_version
        FROM trial_extensions WHERE revision = 1
        """;

    private static async Task<long> ScopeCountAsync(NpgsqlConnectionStringBuilder role, string scope, string table)
    {
        await using var connection = new NpgsqlConnection(role.ConnectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        if (scope.Length != 0)
        {
            await using var configure = new NpgsqlCommand(scope, connection, tx);
            await configure.ExecuteNonQueryAsync();
        }
        await using var count = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection, tx);
        return (long)(await count.ExecuteScalarAsync())!;
    }

    private static async Task RefusedAsync(NpgsqlConnectionStringBuilder role, string sql, string state)
    {
        await using var connection = new NpgsqlConnection(role.ConnectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(sql, connection, tx);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(state, error.SqlState);
    }
}
