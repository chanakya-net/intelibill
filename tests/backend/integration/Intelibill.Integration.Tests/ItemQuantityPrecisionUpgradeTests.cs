using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Intelibill.Integration.Tests;

/// <summary>
/// The quantity-precision migration classifies every existing item from its free-text unit label.
/// A known label gets its catalog code and scale, anything else is flagged for review, and an item
/// whose recorded quantities already carry decimals is flagged even when its label says pieces.
/// No quantity is ever rewritten.
/// </summary>
/// <remarks>
/// Runs on a database of its own so it can migrate to the revision before this work and seed it. The
/// migrations and the rollout script run as a table-owner role that is neither superuser nor BYPASSRLS,
/// as the deployed migrator is: FORCE row level security binds an owner, so a backfill that sets no shop
/// context sees no rows and changes nothing. Running them as the container superuser would hide that.
/// </remarks>
[Collection("Integration Tests")]
public sealed class ItemQuantityPrecisionUpgradeTests(PostgreSqlTestFixture fixture) : IAsyncLifetime
{
    private const string RevisionBeforeThisWork = "20261103145848_AddIssuedInvoiceSnapshotsAndFiscalSeries";

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
    public async Task Upgrade_MapsKnownUnitsAndFlagsAmbiguousItems()
    {
        await using (var context = CreateMigratorContext())
        {
            await context.Database.GetService<IMigrator>().MigrateAsync(RevisionBeforeThisWork);
        }

        var shop = Guid.NewGuid();
        await SeedShopAsync(shop);
        var kgUpper = await SeedItemAsync(shop, "Rice", "KG");
        var kgLower = await SeedItemAsync(shop, "Dal", " kg ");
        var pieces = await SeedItemAsync(shop, "Pen", "PCS");
        var custom = await SeedItemAsync(shop, "Sack", "BAGGIE");
        var piecesWithDecimals = await SeedItemAsync(shop, "Cloth", "PCS");
        var batch = await SeedBatchAsync(shop, piecesWithDecimals, 2.5m);
        var wholeBatch = await SeedBatchAsync(shop, pieces, 4m);
        var kgWithFraction = await SeedItemAsync(shop, "Sugar", "KG");
        await SeedBatchAsync(shop, kgWithFraction, 1.25m);

        var otherShop = Guid.NewGuid();
        await SeedShopAsync(otherShop);
        var otherKg = await SeedItemAsync(otherShop, "Flour", "Kilos");
        var otherCustom = await SeedItemAsync(otherShop, "Roll", "Roll");

        await using (var context = CreateMigratorContext())
        {
            await context.Database.MigrateAsync();
        }

        Assert.Equal(("KG", 3, false), await ReadItemAsync(shop, kgUpper));
        Assert.Equal(("KG", 3, false), await ReadItemAsync(shop, kgLower));
        Assert.Equal(("PCS", 0, false), await ReadItemAsync(shop, pieces));
        Assert.Equal((null, 0, true), await ReadItemAsync(shop, custom));
        Assert.Equal(("PCS", 0, true), await ReadItemAsync(shop, piecesWithDecimals));
        Assert.Equal(("KG", 3, false), await ReadItemAsync(shop, kgWithFraction));
        Assert.Equal(("KG", 3, false), await ReadItemAsync(otherShop, otherKg));
        Assert.Equal((null, 0, true), await ReadItemAsync(otherShop, otherCustom));

        Assert.Equal(2.5m, await ReadBatchQuantityAsync(shop, batch));
        Assert.Equal(4m, await ReadBatchQuantityAsync(shop, wholeBatch));
    }

    [Fact]
    public async Task Upgrade_RefusesAScaleOutsideTheStorageCeiling()
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

    [Fact]
    public async Task RolloutScript_MapsOnlyUnclassifiedItemsAndIsSafeToRerun()
    {
        await using (var context = CreateMigratorContext())
        {
            await context.Database.MigrateAsync();
        }

        var shop = Guid.NewGuid();
        await SeedShopAsync(shop);

        var resolved = await SeedItemAsync(shop, "Cloth", "PCS");
        await ExecuteAsync(
            "select set_config('app.active_shop_id', @shop::text, false); " +
            "update items set unit_code = 'PCS', quantity_scale = 0, unit_review_required = false where id = @id",
            ("shop", shop), ("id", resolved));
        await SeedBatchAsync(shop, resolved, 2.5m);

        var unclassified = await SeedItemAsync(shop, "Rice", "KG");
        var tabbed = await SeedItemAsync(shop, "Dal", "kg\t");
        var wholeWithFraction = await SeedItemAsync(shop, "Cord", "Pieces");
        await SeedBatchAsync(shop, wholeWithFraction, 0.5m);

        var otherShop = Guid.NewGuid();
        await SeedShopAsync(otherShop);
        var otherKg = await SeedItemAsync(otherShop, "Flour", "Kilos");

        var script = await File.ReadAllTextAsync(FindRepoFile("scripts", "db", "backfill-item-quantity-precision.sql"));

        for (var run = 0; run < 2; run++)
        {
            await ExecuteAsync(script);
            Assert.Equal(("PCS", 0, false), await ReadItemAsync(shop, resolved));
            Assert.Equal(("KG", 3, false), await ReadItemAsync(shop, unclassified));
            Assert.Equal(("KG", 3, false), await ReadItemAsync(shop, tabbed));
            Assert.Equal(("PCS", 0, true), await ReadItemAsync(shop, wholeWithFraction));
            Assert.Equal(("KG", 3, false), await ReadItemAsync(otherShop, otherKg));
        }
    }

    private static string FindRepoFile(params string[] segments)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine([dir.FullName, .. segments]);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("Repository file not found: " + Path.Combine(segments));
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

    private async Task<Guid> SeedItemAsync(Guid shopId, string name, string uom)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(
            """
            select set_config('app.active_shop_id', @shop::text, false);
            insert into items (id, shop_id, name, uom, barcode, is_active, created_by, created_at)
            values (@id, @shop, @name, @uom, @barcode, true, @actor, now());
            """,
            ("shop", shopId), ("id", id), ("name", name), ("uom", uom), ("barcode", "BC-" + id.ToString("N")), ("actor", Guid.NewGuid()));
        return id;
    }

    private async Task<Guid> SeedBatchAsync(Guid shopId, Guid itemId, decimal quantity)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(
            """
            select set_config('app.active_shop_id', @shop::text, false);
            insert into inventory_batches (id, shop_id, item_id, batch_number, quantity, original_quantity, cost_price, mrp, sales_price, tax_rate_percent, created_by, created_at)
            values (@id, @shop, @item, @number, @quantity, @quantity, 10, 20, 15, 0, @actor, now());
            """,
            ("shop", shopId), ("id", id), ("item", itemId), ("number", "B-" + id.ToString("N")[..8]), ("quantity", quantity), ("actor", Guid.NewGuid()));
        return id;
    }

    private async Task<(string? UnitCode, int Scale, bool Review)> ReadItemAsync(Guid shopId, Guid itemId)
    {
        await using var connection = new NpgsqlConnection(MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select set_config('app.active_shop_id', @shop::text, false); " +
            "select unit_code, quantity_scale, unit_review_required from items where id = @id", connection);
        command.Parameters.AddWithValue("shop", shopId);
        command.Parameters.AddWithValue("id", itemId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        return (reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetInt16(1), reader.GetBoolean(2));
    }

    private async Task<decimal> ReadBatchQuantityAsync(Guid shopId, Guid batchId)
    {
        await using var connection = new NpgsqlConnection(MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select set_config('app.active_shop_id', @shop::text, false); " +
            "select quantity from inventory_batches where id = @id", connection);
        command.Parameters.AddWithValue("shop", shopId);
        command.Parameters.AddWithValue("id", batchId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        return reader.GetDecimal(0);
    }

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
