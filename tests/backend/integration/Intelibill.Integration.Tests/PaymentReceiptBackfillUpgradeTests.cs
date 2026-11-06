using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Intelibill.Integration.Tests;

/// <summary>
/// The payment-receipt migration gives every legacy ledger payment one receipt and invents nothing:
/// no tender (the method is UNKNOWN_RECORDED_METHOD, never cash), no invoice allocation, and the
/// supplier's linked expense carried over. Account balances are untouched.
/// </summary>
/// <remarks>
/// Runs on a database of its own, migrated and seeded as a table-owner role that is neither
/// superuser nor BYPASSRLS, as the deployed migrator is: a backfill that forgot to set the shop
/// context would see no rows under FORCE row level security and this test would catch it.
/// </remarks>
[Collection("Integration Tests")]
public sealed class PaymentReceiptBackfillUpgradeTests(PostgreSqlTestFixture fixture) : IAsyncLifetime
{
    private const string RevisionBeforeThisWork = "20261105155114_AddItemQuantityPrecision";

    private readonly string _suffix = Guid.NewGuid().ToString("N")[..12];

    private string Database => "pay_upgrade_" + _suffix;

    private string Owner => "pay_owner_" + _suffix;

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
    public async Task LegacyPayment_BackfillDoesNotInventInvoiceOrTender()
    {
        await using (var context = CreateMigratorContext())
            await context.Database.GetService<IMigrator>().MigrateAsync(RevisionBeforeThisWork);

        var shop = Guid.NewGuid();
        var otherShop = Guid.NewGuid();
        await SeedShopAsync(shop);
        await SeedShopAsync(otherShop);
        var customer = await SeedCustomerAsync(shop);
        var customerPayment = await SeedCustomerLedgerAsync(shop, customer, "PAYMENT_RECEIVED", 300m);
        await SeedCustomerLedgerAsync(shop, customer, "RETURN_CREDIT", 50m);
        var supplier = await SeedSupplierAsync(shop);
        var supplierPayment = await SeedSupplierLedgerAsync(shop, supplier, "PAYMENT_MADE", 700m);
        var expense = await SeedExpenseAsync(shop, supplierPayment, 700m);
        var otherCustomer = await SeedCustomerAsync(otherShop);
        var otherPayment = await SeedCustomerLedgerAsync(otherShop, otherCustomer, "PAYMENT_RECEIVED", 90m);

        await using (var context = CreateMigratorContext())
            await context.Database.GetService<IMigrator>().MigrateAsync();

        var script = await File.ReadAllTextAsync(FindRepoFile("scripts", "db", "backfill-payment-receipts.sql"));
        for (var run = 0; run < 2; run++)
        {
            var customerReceipt = Assert.Single(await ReadReceiptsAsync(shop, customerPayment));
            Assert.Equal(("CUSTOMER", customer, 300m, "UNKNOWN_RECORDED_METHOD", true, (Guid?)null), customerReceipt);

            var supplierReceipt = Assert.Single(await ReadReceiptsAsync(shop, supplierPayment));
            Assert.Equal(("SUPPLIER", supplier, 700m, "UNKNOWN_RECORDED_METHOD", true, (Guid?)expense), supplierReceipt);

            Assert.Single(await ReadReceiptsAsync(otherShop, otherPayment));
            Assert.Equal(2, await CountAsync(shop, "select count(*) from payment_receipts"));
            Assert.Equal(0, await CountAsync(shop, "select count(*) from payment_allocations"));

            // Re-running the rollout script is a no-op.
            await ExecuteAsync(script);
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

    private const string InShop = "select set_config('app.active_shop_id', @shop::text, false);\n";

    private Task SeedShopAsync(Guid shopId) => ExecuteAsync(
        InShop + "insert into shops (id, name, address, city, state, pincode, created_at) values (@shop, 'Legacy Shop', '42 MG Road', 'Bengaluru', 'Karnataka', '560001', now());",
        ("shop", shopId));

    private async Task<Guid> SeedCustomerAsync(Guid shopId)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(
            InShop + "insert into customers (id, shop_id, name, phone_number, created_at) values (@id, @shop, 'Legacy Customer', @phone, now());",
            ("shop", shopId), ("id", id), ("phone", "+9198" + Random.Shared.Next(10_000_000, 99_999_999)));
        return id;
    }

    private async Task<Guid> SeedCustomerLedgerAsync(Guid shopId, Guid customerId, string type, decimal amount)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(
            InShop + "insert into customer_ledger_entries (id, shop_id, customer_id, entry_type, amount, entry_date, created_by, created_at) values (@id, @shop, @customer, @type, @amount, current_date, @actor, now());",
            ("shop", shopId), ("id", id), ("customer", customerId), ("type", type), ("amount", amount), ("actor", Guid.NewGuid()));
        return id;
    }

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

    private async Task<List<(string PartyType, Guid PartyId, decimal Amount, string Method, bool IsLegacy, Guid? ExpenseId)>> ReadReceiptsAsync(Guid shopId, Guid ledgerEntryId)
    {
        await using var connection = new NpgsqlConnection(MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            InShop + "select party_type, party_id, amount, method, is_legacy, expense_id from payment_receipts where ledger_entry_id = @entry",
            connection);
        command.Parameters.AddWithValue("shop", shopId);
        command.Parameters.AddWithValue("entry", ledgerEntryId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.NextResultAsync());
        var rows = new List<(string, Guid, decimal, string, bool, Guid?)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetGuid(1), reader.GetDecimal(2), reader.GetString(3), reader.GetBoolean(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5)));
        }

        return rows;
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
