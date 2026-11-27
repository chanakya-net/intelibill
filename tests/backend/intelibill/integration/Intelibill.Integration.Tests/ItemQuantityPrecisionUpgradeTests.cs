using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Intelibill.Integration.Tests;

/// <summary>
/// Items store a quantity scale that the schema bounds to the storage ceiling.
/// </summary>
/// <remarks>
/// Runs on a database of its own, migrated and seeded as a table-owner role that is neither superuser
/// nor BYPASSRLS, as the deployed migrator is.
/// </remarks>
[Collection("Integration Tests")]
public sealed class ItemQuantityPrecisionUpgradeTests(PostgreSqlTestFixture fixture) : IAsyncLifetime
{
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..12];

    private string _database => "qty_upgrade_" + _suffix;

    private string _owner => "qty_owner_" + _suffix;

    private string MigratorConnectionString =>
        new NpgsqlConnectionStringBuilder(fixture.MigratorConnectionString)
        {
            Database = _database,
            Username = _owner,
            Password = "owner_password",
        }.ConnectionString;

    public async Task InitializeAsync()
    {
        await ExecuteOnServerAsync(
            $"create role {_owner} login password 'owner_password' nosuperuser nocreatedb nocreaterole nobypassrls");
        await ExecuteOnServerAsync($"create database {_database} owner {_owner}");
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await ExecuteOnServerAsync("drop database if exists " + _database + " with (force)");
        await ExecuteOnServerAsync($"drop role if exists {_owner}");
    }

    [Fact]
    public async Task Schema_RefusesAScaleOutsideTheStorageCeiling()
    {
        await using (var context = CreateMigratorContext())
        {
            await context.Database.MigrateAsync();
        }

        var shop = Guid.NewGuid();
        await SeedShopAsync(shop);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            """
            select set_config('app.active_shop_id', @shop::text, false);
            insert into items (id, shop_id, name, uom, barcode, is_active, created_by, created_at, quantity_scale)
            values (gen_random_uuid(), @shop, 'Bad', 'KG', 'BAD-1', true, gen_random_uuid(), now(), 4);
            """,
            ("shop", shop)));
        Assert.Equal("ck_items_quantity_scale", ex.ConstraintName);
    }

    private ApplicationDbContext CreateMigratorContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(MigratorConnectionString)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options);

    private async Task SeedShopAsync(Guid shopId) => await ExecuteAsync(
        """
        select set_config('app.active_shop_id', @shop::text, false);
        insert into shops (id, name, address, city, state, pincode, created_at)
        values (@shop, 'Legacy Shop', '42 MG Road', 'Bengaluru', 'Karnataka', '560001', now());
        """,
        ("shop", shopId));

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task ExecuteOnServerAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
