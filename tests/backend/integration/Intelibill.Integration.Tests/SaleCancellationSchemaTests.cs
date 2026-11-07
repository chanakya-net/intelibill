using System.Text.Json.Nodes;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class SaleCancellationSchemaTests(PostgreSqlTestFixture fixture)
{
    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(fixture.MigratorConnectionString).UseSnakeCaseNamingConvention().Options);

    private sealed record Facts(Shop Shop, Sale Sale, SaleCancellation Cancellation, SaleCancellationLine Line, StockTransaction Movement, CustomerLedgerEntry Ledger);

    private async Task<Facts> SeedAsync(bool persistCancellation = true)
    {
        await using var db = Context();
        var actor = Guid.NewGuid();
        var shop = Shop.Create("Frozen shop", "Address", "City", "State", "123456", null, null, null);
        var customer = Customer.Create(shop.Id, "Frozen buyer", Guid.NewGuid().ToString("N")[..10], null);
        var item = Item.Create(shop.Id, "Sugar", null, "kg", Guid.NewGuid().ToString("N"), true, actor);
        var batch = InventoryBatch.Create(shop.Id, item.Id, "LOT-1", 10m, 60m, 100m, 100m, 0m, false, null, null, null, actor).Value;
        var now = DateTimeOffset.UtcNow;
        var sale = Sale.Record(shop.Id, actor, Guid.NewGuid().ToString("N"), "HASH", Guid.NewGuid().ToString("N"),
            [new SaleLineInput(shop.Id, SaleLineType.Goods, item.Id, batch.Id, null, "Sugar", "SUGAR", 2m, 60m, 100m, 100m, 0m, false, false)],
            customer.Id, "Frozen buyer", null, PaymentMethod.Cash, 80m, 120m, now, BusinessDay.InDefaultZone(now)).Value;
        var ledger = CustomerLedgerEntry.Create(shop.Id, customer.Id, sale.Id, CustomerLedgerEntryType.SaleCancellationCredit, 120m, sale.BusinessDate, null, actor).Value;
        var movement = StockTransaction.Create(shop.Id, item.Id, batch.Id, StockTransactionType.SaleCancellation, 2m, "SC", null, now, actor, actor).Value;
        var cancellation = NewCancellation(shop.Id, sale.Id, actor, ledger.Id);
        var line = SaleCancellationLine.Create(shop.Id, cancellation.Id, sale.Id, sale.Items[0].Id, item.Id, batch.Id, movement.Id, 2m, "Sugar", "KGS", 60m, 200m, 120m).Value;
        Assert.False(cancellation.AddLine(line).IsError);
        db.AddRange(shop, customer, item, batch, sale, ledger, movement);
        if (persistCancellation) db.Add(cancellation);
        await db.SaveChangesAsync();
        return new(shop, sale, cancellation, line, movement, ledger);
    }

    private static SaleCancellation NewCancellation(Guid shop, Guid sale, Guid actor, Guid ledger) =>
        SaleCancellation.Create(shop, sale, actor, Guid.NewGuid().ToString("N"), "Mistaken entry", DateTimeOffset.UtcNow,
            new DateOnly(2026, 11, 8), "Asia/Kolkata", "A/2627/000001", new DateOnly(2026, 11, 7),
            200m, 200m, 0m, 120m, 80m, ledger,
            new("Seller", "Shop", "Address", "29", "Karnataka", null, TaxRegime.Unregistered),
            new("Buyer", null, null, null, null, null), true, true).Value;

    [Fact]
    public async Task Sale_DefaultsPostedWithXmin_AndCancellationSurvivesRoundTrip()
    {
        var facts = await SeedAsync();
        await using var db = Context();
        var sale = await db.Sales.SingleAsync(x => x.Id == facts.Sale.Id);
        Assert.Equal(SaleStatus.Posted, sale.Status);
        Assert.NotEqual(0u, sale.Version);
        var cancellation = await db.Set<SaleCancellation>().Include(x => x.Lines).SingleAsync(x => x.Id == facts.Cancellation.Id);
        Assert.Equal("Frozen shop", facts.Shop.Name);
        Assert.Equal("Seller", cancellation.Seller.LegalName);
        Assert.Equal("Buyer", cancellation.Buyer.Name);
        Assert.Equal(120m, Assert.Single(cancellation.Lines).RecordedCostAmount);
        Assert.False(sale.MarkCancelled().IsError);
        await db.SaveChangesAsync();
        await db.Entry(sale).ReloadAsync();
        Assert.Equal(SaleStatus.Cancelled, sale.Status);
        Assert.NotEqual(facts.Sale.Version, sale.Version);
    }

    [Fact]
    public async Task Migration_ExistingSaleBecomesPostedWithSystemXmin()
    {
        var facts = await SeedAsync();
        await using var admin = new NpgsqlConnection(fixture.MigratorConnectionString);
        await admin.OpenAsync();
        async Task<string> SnapshotAsync(string table, Guid id)
        {
            await using var read = new NpgsqlCommand($"select row_to_json(t)::text from {table} t where id=@id", admin);
            read.Parameters.AddWithValue("id", id);
            return Assert.IsType<string>(await read.ExecuteScalarAsync());
        }
        var shopJson = await SnapshotAsync("shops", facts.Shop.Id);
        var saleJson = JsonNode.Parse(await SnapshotAsync("sales", facts.Sale.Id))!.AsObject();
        saleJson.Remove("status");
        saleJson["customer_id"] = null;
        var databaseName = $"cancellation_upgrade_{Guid.NewGuid():N}";
        await using (var create = new NpgsqlCommand($"create database {databaseName}", admin))
            await create.ExecuteNonQueryAsync();
        try
        {
            var connectionString = new NpgsqlConnectionStringBuilder(fixture.MigratorConnectionString) { Database = databaseName, Pooling = false }.ConnectionString;
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(connectionString).UseSnakeCaseNamingConvention().Options);
            var migrator = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
            await migrator.MigrateAsync("20261107190743_AddSupplierReturnLotSnapshots");
            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var insert = new NpgsqlCommand("insert into shops select * from json_populate_record(null::shops, @shop::json); insert into sales select * from json_populate_record(null::sales, @sale::json)", connection);
                insert.Parameters.AddWithValue("shop", shopJson);
                insert.Parameters.AddWithValue("sale", saleJson.ToJsonString());
                await insert.ExecuteNonQueryAsync();
            }
            await migrator.MigrateAsync();
            var sale = await db.Sales.SingleAsync(x => x.Id == facts.Sale.Id);
            Assert.Equal(SaleStatus.Posted, sale.Status);
            Assert.NotEqual(0u, sale.Version);
            Assert.Equal(200m, sale.TotalAmount);
            Assert.Equal(80m, sale.PaidAmount);
            Assert.Equal(120m, sale.DueAmount);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"drop database {databaseName} with (force)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Sale_RejectsSecondCancellation()
    {
        var facts = await SeedAsync();
        await using var db = Context();
        db.Add(NewCancellation(facts.Shop.Id, facts.Sale.Id, Guid.NewGuid(), facts.Ledger.Id));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Theory]
    [InlineData("header-sale")]
    [InlineData("header-ledger")]
    [InlineData("line-header")]
    [InlineData("line-sale-item")]
    [InlineData("line-movement")]
    [InlineData("line-batch")]
    public async Task Links_RejectForeignShopAndUnrelatedSameShopSaleItems(string target)
    {
        var own = await SeedAsync();
        var foreign = await SeedAsync(persistCancellation: target is not ("header-sale" or "line-movement"));
        await using var connection = new NpgsqlConnection(fixture.MigratorConnectionString);
        await connection.OpenAsync();
        var (sql, id, value) = target switch
        {
            "header-sale" => ("update sale_cancellations set sale_id=@value where id=@id", own.Cancellation.Id, foreign.Sale.Id),
            "header-ledger" => ("update sale_cancellations set customer_ledger_entry_id=@value where id=@id", own.Cancellation.Id, foreign.Ledger.Id),
            "line-header" => ("update sale_cancellation_lines set sale_cancellation_id=@value where id=@id", own.Line.Id, foreign.Cancellation.Id),
            "line-sale-item" => ("update sale_cancellation_lines set sale_item_id=@value where id=@id", own.Line.Id, foreign.Sale.Items[0].Id),
            "line-movement" => ("update sale_cancellation_lines set stock_transaction_id=@value where id=@id", own.Line.Id, foreign.Movement.Id),
            _ => ("update sale_cancellation_lines set inventory_batch_id=@value where id=@id", own.Line.Id, foreign.Line.InventoryBatchId),
        };
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("value", value);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Fact]
    public async Task Line_RejectsDifferentSaleItemEvenWithinSameShop()
    {
        var facts = await SeedAsync();
        await using var db = Context();
        var original = facts.Sale.Items[0];
        var now = DateTimeOffset.UtcNow;
        var other = Sale.Record(facts.Shop.Id, Guid.NewGuid(), Guid.NewGuid().ToString("N"), "HASH", Guid.NewGuid().ToString("N"),
            [new SaleLineInput(facts.Shop.Id, SaleLineType.Goods, original.ItemId, original.InventoryBatchId, null, "Sugar", "SUGAR", 2m, 60m, 100m, 100m, 0m, false, false)],
            null, null, null, PaymentMethod.Cash, 200m, 0m, now, BusinessDay.InDefaultZone(now)).Value;
        db.Add(other);
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"update sale_cancellation_lines set sale_item_id={other.Items[0].Id} where id={facts.Line.Id}"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Theory]
    [InlineData("sale_cancellations")]
    [InlineData("sale_cancellation_lines")]
    public async Task RestrictedRole_OnlyReadsOwnShopAndRefusesForeignInsert(string table)
    {
        var own = await SeedAsync();
        var foreign = await SeedAsync();
        await using var connection = new NpgsqlConnection(fixture.RuntimeConnectionString);
        await connection.OpenAsync();
        await using (var scope = new NpgsqlCommand("select set_config('app.active_shop_id', @shop, false)", connection))
        {
            scope.Parameters.AddWithValue("shop", own.Shop.Id.ToString());
            await scope.ExecuteScalarAsync();
        }
        await using (var policy = new NpgsqlCommand("select c.relrowsecurity and c.relforcerowsecurity and exists(select 1 from pg_policies p where p.tablename=c.relname and p.qual is not null and p.with_check is not null) from pg_class c where c.relname=@table", connection))
        {
            policy.Parameters.AddWithValue("table", table);
            Assert.Equal(true, await policy.ExecuteScalarAsync());
        }
        await using (var read = new NpgsqlCommand($"select count(*) from {table}", connection))
            Assert.Equal(1L, await read.ExecuteScalarAsync());
        // RLS applies before uniqueness or FK validation; an invisible row is not copied.
        var id = table == "sale_cancellations" ? foreign.Cancellation.Id : foreign.Line.Id;
        await using (var insert = new NpgsqlCommand($"insert into {table} select * from {table} where id=@id", connection))
        {
            insert.Parameters.AddWithValue("id", id);
            Assert.Equal(0, await insert.ExecuteNonQueryAsync());
        }
        await using var admin = new NpgsqlConnection(fixture.MigratorConnectionString);
        await admin.OpenAsync();
        await using var snapshot = new NpgsqlCommand($"select row_to_json(t)::text from {table} t where id=@id", admin);
        snapshot.Parameters.AddWithValue("id", id);
        var json = Assert.IsType<string>(await snapshot.ExecuteScalarAsync());
        await using (var insert = new NpgsqlCommand($"insert into {table} select * from json_populate_record(null::{table}, @row::json)", connection))
        {
            insert.Parameters.AddWithValue("row", json);
            var error = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        }
        await using (var clear = new NpgsqlCommand("select set_config('app.active_shop_id', '', false)", connection))
            await clear.ExecuteScalarAsync();
        await using var blankRead = new NpgsqlCommand($"select count(*) from {table}", connection);
        Assert.Equal(0L, await blankRead.ExecuteScalarAsync());
    }
}
