using System.Globalization;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Intelibill.Integration.Tests;

/// <summary>
/// Every other test in this project starts from an empty database, so they all
/// prove the same thing: a freshly migrated schema is isolated. Deployment does
/// not do that. It applies these migrations to a database that already holds
/// merchant data, and a policy that only lands on a fresh schema would pass the
/// whole suite and protect nothing in production.
/// </summary>
/// <remarks>
/// Runs on a database of its own so it can migrate to the revision before this
/// work, seed rows the old way, and then upgrade.
/// </remarks>
[Collection("Integration Tests")]
public sealed class UpgradedSchemaRlsTests(PostgreSqlTestFixture fixture) : IAsyncLifetime
{
    /// <summary>The last migration before tenant isolation was completed.</summary>
    private const string RevisionBeforeThisWork = "20261101090000_HashRefreshTokensAndChainRotations";

    private readonly string _database = "upgrade_" + Guid.NewGuid().ToString("N")[..12];

    private string MigratorConnectionString =>
        new NpgsqlConnectionStringBuilder(fixture.MigratorConnectionString) { Database = _database }.ConnectionString;

    private string RuntimeConnectionString =>
        RestrictedRuntimeRole.ConnectionStringFor(MigratorConnectionString);

    public async Task InitializeAsync()
    {
        await ExecuteOnServerAsync("create database " + _database);
        await RestrictedRuntimeRole.EnsureAsync(fixture.MigratorConnectionString);
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await ExecuteOnServerAsync("drop database if exists " + _database + " with (force)");
    }

    private async Task ExecuteOnServerAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private ApplicationDbContext CreateMigratorContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(MigratorConnectionString)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options);

    [Fact]
    public async Task UpgradedSchema_IsolatesDataThatPredatesThePolicies()
    {
        // 1. The schema as it stood before this work, with no policy on items.
        await using (var context = CreateMigratorContext())
        {
            await context.Database.GetService<IMigrator>().MigrateAsync(RevisionBeforeThisWork);
        }

        await RestrictedRuntimeRole.GrantAsync(MigratorConnectionString);

        var shopId = await SeedShopWithItemAsync();

        // Pre-upgrade: no boundary, which is exactly the state deployment starts from.
        Assert.Equal(1L, await CountItemsAsRuntimeAsync(activeShopId: null));

        // 2. Upgrade in place, as the migration job does.
        await using (var context = CreateMigratorContext())
        {
            await context.Database.MigrateAsync();
        }

        await RestrictedRuntimeRole.GrantAsync(MigratorConnectionString);

        // 3. The row that predates the policy is now covered by it.
        Assert.Equal(0L, await CountItemsAsRuntimeAsync(activeShopId: null));
        Assert.Equal(0L, await CountItemsAsRuntimeAsync(activeShopId: Guid.NewGuid()));
        Assert.Equal(1L, await CountItemsAsRuntimeAsync(activeShopId: shopId));
    }

    [Fact]
    public async Task UpgradedSchema_CatalogMatchesTheManifest()
    {
        await using (var context = CreateMigratorContext())
        {
            await context.Database.GetService<IMigrator>().MigrateAsync(RevisionBeforeThisWork);
            await context.Database.MigrateAsync();
        }

        await using var connection = new NpgsqlConnection(MigratorConnectionString);
        await connection.OpenAsync();

        var offenders = new List<string>();
        foreach (var entry in TenantPolicyManifest.TenantTables)
        {
            await using var command = new NpgsqlCommand(
                """
                select c.relrowsecurity,
                       c.relforcerowsecurity,
                       (select count(*) from pg_policy p
                        where p.polrelid = c.oid
                          and p.polqual is not null
                          and p.polwithcheck is not null
                          and pg_get_expr(p.polqual, p.polrelid) ilike '%nullif%'
                          and pg_get_expr(p.polwithcheck, p.polrelid) ilike '%nullif%')
                from pg_class c
                join pg_namespace n on n.oid = c.relnamespace
                where n.nspname = 'public' and c.relname = @table
                """,
                connection);
            command.Parameters.AddWithValue("table", entry.Table);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                offenders.Add(entry.Table + ": table missing after upgrade");
                continue;
            }

            var enabled = reader.GetBoolean(0);
            var forced = reader.GetBoolean(1);
            var policies = reader.GetInt64(2);

            if (!enabled || !forced || policies != 1)
            {
                offenders.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{entry.Table}: enabled={enabled}, forced={forced}, conforming policies={policies}"));
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Writes a shop and one item through the owner identity, the way rows that
    /// predate the policies got there.
    /// </summary>
    private async Task<Guid> SeedShopWithItemAsync()
    {
        var shopId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(MigratorConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            insert into shops (id, name, address, city, state, pincode, created_at)
            values (@shop, 'Legacy Shop', '42 MG Road', 'Bengaluru', 'Karnataka', '560001', now());

            insert into items (id, shop_id, name, barcode, uom, is_active, created_by, created_at)
            values (@item, @shop, 'Legacy Rice', @barcode, 'kg', true, @user, now());
            """;
        command.Parameters.AddWithValue("shop", shopId);
        command.Parameters.AddWithValue("item", Guid.NewGuid());
        command.Parameters.AddWithValue("barcode", "LEGACY-" + Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("user", Guid.NewGuid());
        await command.ExecuteNonQueryAsync();

        return shopId;
    }

    private async Task<long> CountItemsAsRuntimeAsync(Guid? activeShopId)
    {
        await using var connection = new NpgsqlConnection(RuntimeConnectionString);
        await connection.OpenAsync();

        await using var context = connection.CreateCommand();
        context.CommandText = "select set_config('app.active_shop_id', @shop, false)";
        context.Parameters.AddWithValue("shop", activeShopId?.ToString() ?? string.Empty);
        await context.ExecuteNonQueryAsync();

        await using var count = connection.CreateCommand();
        count.CommandText = "select count(*) from items";
        return (long)(await count.ExecuteScalarAsync())!;
    }
}
