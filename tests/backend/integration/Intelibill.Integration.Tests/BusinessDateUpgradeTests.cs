using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Intelibill.Integration.Tests;

/// <summary>
/// Sales and returns carry the business date they were posted under. The migration backfills every
/// existing row in Asia/Kolkata, and a row written without the new columns (a pod still on the previous
/// build during a rolling deploy) is given the same date by a trigger.
/// </summary>
/// <remarks>
/// Runs on a database of its own, migrated and seeded as a table-owner role that is neither
/// superuser nor BYPASSRLS, as the deployed migrator is.
/// </remarks>
[Collection("Integration Tests")]
public sealed class BusinessDateUpgradeTests(PostgreSqlTestFixture fixture) : IAsyncLifetime
{
    private const string RevisionBeforeThisWork = "20261106131449_AddCustomerCreditPolicyAndSaleDueDates";

    private static readonly DateTimeOffset LateEveningUtc = new(2026, 10, 20, 20, 30, 0, TimeSpan.Zero);

    private readonly string _suffix = Guid.NewGuid().ToString("N")[..12];

    private string Database => "bizdate_upgrade_" + _suffix;

    private string Owner => "bizdate_owner_" + _suffix;

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
    public async Task LegacyInsertWithoutBusinessDate_GetsIstDate()
    {
        await using (var context = CreateMigratorContext())
            await context.Database.GetService<IMigrator>().MigrateAsync();

        var shop = Guid.NewGuid();
        await SeedShopAsync(shop);
        var sale = await InsertLegacySaleAsync(shop, LateEveningUtc);

        Assert.Equal(("2026-10-21", "Asia/Kolkata"), await ReadSaleAsync(shop, sale));

        var saleReturn = await InsertLegacyReturnAsync(shop, sale, LateEveningUtc);
        Assert.Equal(("2026-10-21", "Asia/Kolkata", (string?)null), await ReadReturnAsync(shop, saleReturn));

        // An old pod voids a return by setting voided_at alone.
        var voidedAt = new DateTimeOffset(2026, 10, 22, 19, 0, 0, TimeSpan.Zero);
        await ExecuteAsync(
            InShop + "update sale_returns set is_voided = true, voided_at = @at, voided_by = @by, void_reason = 'Mistake' where id = @id;",
            ("shop", shop), ("id", saleReturn), ("at", voidedAt), ("by", Guid.NewGuid()));
        Assert.Equal(("2026-10-21", "Asia/Kolkata", (string?)"2026-10-23"), await ReadReturnAsync(shop, saleReturn));
    }

    [Fact]
    public async Task ExistingRows_AreBackfilledInIst_AndTheScriptIsIdempotent()
    {
        await using (var context = CreateMigratorContext())
            await context.Database.GetService<IMigrator>().MigrateAsync(RevisionBeforeThisWork);

        var shop = Guid.NewGuid();
        var otherShop = Guid.NewGuid();
        await SeedShopAsync(shop);
        await SeedShopAsync(otherShop);
        var sale = await InsertLegacySaleAsync(shop, LateEveningUtc);
        var returned = await InsertLegacyReturnAsync(shop, sale, LateEveningUtc, voidedAt: new DateTimeOffset(2026, 10, 22, 19, 0, 0, TimeSpan.Zero));
        var otherSale = await InsertLegacySaleAsync(otherShop, new DateTimeOffset(2026, 3, 31, 18, 29, 59, TimeSpan.Zero));

        await using (var context = CreateMigratorContext())
            await context.Database.GetService<IMigrator>().MigrateAsync();

        var script = await File.ReadAllTextAsync(FindRepoFile("scripts", "db", "backfill-business-dates.sql"));
        for (var run = 0; run < 2; run++)
        {
            Assert.Equal(("2026-10-21", "Asia/Kolkata"), await ReadSaleAsync(shop, sale));
            Assert.Equal(("2026-10-21", "Asia/Kolkata", (string?)"2026-10-23"), await ReadReturnAsync(shop, returned));
            Assert.Equal(("2026-03-31", "Asia/Kolkata"), await ReadSaleAsync(otherShop, otherSale));
            Assert.Equal("Asia/Kolkata", await ReadShopZoneAsync(shop));

            // Re-running the rollout script is a no-op.
            await ExecuteAsync(script);
        }
    }

    [Fact]
    public async Task Migration_LeavesTheColumnsRequired()
    {
        await using (var context = CreateMigratorContext())
            await context.Database.GetService<IMigrator>().MigrateAsync();

        var shop = Guid.NewGuid();
        await SeedShopAsync(shop);
        Assert.Equal(
            4,
            await CountAsync(
                shop,
                "select count(*) from information_schema.columns where is_nullable = 'NO' and "
                + "((table_name in ('sales', 'sale_returns') and column_name in ('business_date', 'business_time_zone')))"));
        Assert.Equal(
            1,
            await CountAsync(
                shop,
                "select count(*) from information_schema.columns where is_nullable = 'YES' and table_name = 'sale_returns' and column_name = 'voided_business_date'"));
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

    // Written the way the previous build wrote it: no business_date, no business_time_zone.
    private async Task<Guid> InsertLegacySaleAsync(Guid shopId, DateTimeOffset soldAt)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(
            InShop
            + "insert into sales (id, shop_id, actor_user_id, idempotency_key, request_hash, invoice_number, payment_method, warnings, sold_at, paid_amount, due_amount, "
            + "subtotal_before_discount, total_before_discount, total_discount_amount, total_amount, total_tax_amount, sale_discount_override_type, sale_discount_override_value, created_at) "
            + "values (@id, @shop, @actor, @key, @hash, @invoice, 0, '{}', @soldAt, 100, 0, 100, 100, 0, 100, 0, 0, 0, now());",
            ("shop", shopId), ("id", id), ("actor", Guid.NewGuid()), ("key", "k-" + id.ToString("N")), ("hash", "h-" + id.ToString("N")),
            ("invoice", "INV-" + id.ToString("N")[..8]), ("soldAt", soldAt));
        return id;
    }

    private async Task<Guid> InsertLegacyReturnAsync(Guid shopId, Guid saleId, DateTimeOffset processedAt, DateTimeOffset? voidedAt = null)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(
            InShop
            + "insert into sale_returns (id, shop_id, sale_id, return_number, processed_at, processed_by, total_refund_amount, due_reduction_amount, payout_amount, "
            + "total_taxable_amount, total_tax_amount, is_voided, voided_at, voided_by, void_reason, created_at) "
            + "values (@id, @shop, @sale, @number, @processedAt, @actor, 0, 0, 0, 0, 0, @voided, @voidedAt, @voidedBy, @reason, now());",
            ("shop", shopId), ("id", id), ("sale", saleId), ("number", "RET-" + id.ToString("N")[..8]), ("processedAt", processedAt),
            ("actor", Guid.NewGuid()), ("voided", voidedAt.HasValue), ("voidedAt", (object?)voidedAt ?? DBNull.Value),
            ("voidedBy", voidedAt.HasValue ? Guid.NewGuid() : DBNull.Value), ("reason", voidedAt.HasValue ? "Mistake" : DBNull.Value));
        return id;
    }

    private async Task<(string Date, string Zone)> ReadSaleAsync(Guid shopId, Guid id)
    {
        var row = await ReadRowAsync(shopId, "select business_date::text, business_time_zone from sales where id = @id", id);
        return (row[0]!, row[1]!);
    }

    private async Task<(string Date, string Zone, string? VoidedDate)> ReadReturnAsync(Guid shopId, Guid id)
    {
        var row = await ReadRowAsync(shopId, "select business_date::text, business_time_zone, voided_business_date::text from sale_returns where id = @id", id);
        return (row[0]!, row[1]!, row[2]);
    }

    private async Task<string> ReadShopZoneAsync(Guid shopId) =>
        (await ReadRowAsync(shopId, "select business_time_zone from shops where id = @id", shopId))[0]!;

    private async Task<string?[]> ReadRowAsync(Guid shopId, string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(InShop + sql, connection);
        command.Parameters.AddWithValue("shop", shopId);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        return Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? null : reader.GetString(i)).ToArray();
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
