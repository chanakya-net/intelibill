using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Intelibill.Integration.Tests;

/// <summary>
/// Expense treatment upgrade follows supplier provenance through same-shop correction ancestry.
/// </summary>
/// <remarks>
/// Runs on a database of its own, migrated and seeded as a table-owner role that is neither
/// superuser nor BYPASSRLS, as the deployed migrator is: a backfill that forgot to set the shop
/// context would see no rows under FORCE row level security and this test would catch it.
/// </remarks>
[Collection("Integration Tests")]
public sealed class ExpenseTreatmentBackfillUpgradeTests(PostgreSqlTestFixture fixture) : IAsyncLifetime
{
    private const string RevisionBeforeThisWork = "20261106173956_AddPostingPeriods";

    private readonly string _suffix = Guid.NewGuid().ToString("N")[..12];

    private string Database => "expense_upgrade_" + _suffix;

    private string Owner => "expense_owner_" + _suffix;

    private string MigratorConnectionString =>
        new NpgsqlConnectionStringBuilder(fixture.MigratorConnectionString)
        {
            Database = Database,
            Username = Owner,
            Password = "owner_password",
        }.ConnectionString;

    public async Task InitializeAsync()
    {
        await ExecuteOnServerAsync(
            $"create role {Owner} login password 'owner_password' nosuperuser nocreatedb nocreaterole nobypassrls");
        await ExecuteOnServerAsync($"create database {Database} owner {Owner}");
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await ExecuteOnServerAsync("drop database if exists " + Database + " with (force)");
        await ExecuteOnServerAsync($"drop role if exists {Owner}");
    }

    [Fact]
    public async Task Migration_BackfillsOnlySameShopSupplierAncestryUnderForcedRls_AndRerunTerminatesCycles()
    {
        await using (var context = CreateMigratorContext())
            await context.Database.GetService<IMigrator>().MigrateAsync(RevisionBeforeThisWork);
        var shop = Guid.NewGuid(); var otherShop = Guid.NewGuid();
        await SeedShopAsync(shop); await SeedShopAsync(otherShop);
        var supplier = await SeedSupplierAsync(shop);
        var ledger = await SeedSupplierLedgerAsync(shop, supplier, "PAYMENT_MADE", 100m);
        var root = await SeedExpenseAsync(shop, ledger, 100m);
        var child = await SeedOrdinaryAsync(shop, root);
        var grandchild = await SeedOrdinaryAsync(shop, child);
        var ordinary = await SeedOrdinaryAsync(shop, null);
        var foreignChild = await SeedOrdinaryAsync(otherShop, root);
        // Legacy cycle rooted in real provenance: recursive UNION must visit it once.
        await ExecuteAsync(InShop + "update expenses set original_expense_id = @parent where id = @id", ("shop", shop), ("parent", grandchild), ("id", root));
        await using (var context = CreateMigratorContext())
            await context.Database.GetService<IMigrator>().MigrateAsync();
        Assert.Equal(3, await CountAsync(shop, "select count(*) from expenses where treatment = 2"));
        Assert.Equal(1, await CountAsync(shop, "select count(*) from expenses where treatment = 0"));
        Assert.Equal(1, await CountAsync(otherShop, "select count(*) from expenses where treatment = 0"));
        var script = await File.ReadAllTextAsync(FindRepoFile("scripts", "db", "backfill-expense-treatment.sql"));
        await ExecuteAsync(script); await ExecuteAsync(script);
        Assert.Equal(3, await CountAsync(shop, "select count(*) from expenses where treatment = 2"));
        Assert.Equal(1, await CountAsync(otherShop, "select count(*) from expenses where treatment = 0"));
        var invalid = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(InShop + "update expenses set treatment = 999 where id = @id", ("shop", shop), ("id", ordinary)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalid.SqlState);
        Assert.NotEqual(root, foreignChild);
    }

    private async Task<Guid> SeedOrdinaryAsync(Guid shop, Guid? parent)
    {
        var id = Guid.NewGuid(); var category = Guid.NewGuid();
        await ExecuteAsync(InShop + "insert into expense_categories (id, shop_id, name, created_at) values (@category, @shop, @name, now()); "
            + "insert into expenses (id, shop_id, category_id, amount, paid_to, expense_date, actor_user_id, original_expense_id, created_at) values (@id, @shop, @category, 20, 'Ordinary', current_date, @actor, @parent, now());",
            ("shop", shop), ("id", id), ("category", category), ("name", "Supplier Payments " + id), ("actor", Guid.NewGuid()), ("parent", (object?)parent ?? DBNull.Value));
        return id;
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

    private const string InShop = "select set_config('app.active_shop_id', @shop::text, false);\n";

    private Task SeedShopAsync(Guid shopId) => ExecuteAsync(
        InShop + "insert into shops (id, name, address, city, state, pincode, created_at) values (@shop, 'Legacy Shop', '42 MG Road', 'Bengaluru', 'Karnataka', '560001', now());",
        ("shop", shopId));

    private async Task<Guid> SeedSupplierAsync(Guid shopId)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(
            InShop + "insert into suppliers (id, shop_id, name, is_active, is_preferred, created_at) values (@id, @shop, 'Legacy Supplier', true, false, now());",
            ("shop", shopId), ("id", id));
        return id;
    }

    private async Task<Guid> SeedSupplierLedgerAsync(Guid shopId, Guid supplierId, string type, decimal amount)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(
            InShop + "insert into supplier_ledger_entries (id, shop_id, supplier_id, entry_type, amount, entry_date, created_by, created_at) values (@id, @shop, @supplier, @type, @amount, current_date, @actor, now());",
            ("shop", shopId), ("id", id), ("supplier", supplierId), ("type", type), ("amount", amount), ("actor", Guid.NewGuid()));
        return id;
    }

    private async Task<Guid> SeedExpenseAsync(Guid shopId, Guid ledgerEntryId, decimal amount)
    {
        var category = Guid.NewGuid();
        var id = Guid.NewGuid();
        await ExecuteAsync(
            InShop
            + "insert into expense_categories (id, shop_id, name, created_at) values (@category, @shop, 'Supplier Payments', now());\n"
            + "insert into expenses (id, shop_id, category_id, amount, paid_to, expense_date, actor_user_id, supplier_ledger_entry_id, created_at) values (@id, @shop, @category, @amount, 'Supplier', current_date, @actor, @entry, now());",
            ("shop", shopId), ("category", category), ("id", id), ("amount", amount), ("actor", Guid.NewGuid()), ("entry", ledgerEntryId));
        return id;
    }

    private async Task<long> CountAsync(Guid shopId, string sql)
    {
        await using var connection = new NpgsqlConnection(MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(InShop + sql, connection);
        command.Parameters.AddWithValue("shop", shopId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        return reader.GetInt64(0);
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
