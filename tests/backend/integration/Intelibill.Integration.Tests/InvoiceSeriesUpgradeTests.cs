using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Intelibill.Integration.Tests;

/// <summary>
/// The fiscal-series migration replaces the per-shop-year index with two that include the series and
/// the seller registration. Shops that already hold offline counters must come through it with
/// distinct legacy keys, or the new unique index cannot be built on a populated database.
/// </summary>
/// <remarks>Runs on a database of its own so it can migrate to the revision before this work and seed it.</remarks>
[Collection("Integration Tests")]
public sealed class InvoiceSeriesUpgradeTests(PostgreSqlTestFixture fixture) : IAsyncLifetime
{
    private const string RevisionBeforeThisWork = "20261103130000_AddOfflineSubmissionReconciliationAudit";

    private readonly string _database = "series_upgrade_" + Guid.NewGuid().ToString("N")[..12];

    private string MigratorConnectionString =>
        new NpgsqlConnectionStringBuilder(fixture.MigratorConnectionString) { Database = _database }.ConnectionString;

    public async Task InitializeAsync() => await ExecuteOnServerAsync("create database " + _database);

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await ExecuteOnServerAsync("drop database if exists " + _database + " with (force)");
    }

    [Fact]
    public async Task Upgrade_GivesEveryLegacyCounterItsOwnRegistrationKey()
    {
        await using (var context = CreateMigratorContext())
        {
            await context.Database.GetService<IMigrator>().MigrateAsync(RevisionBeforeThisWork);
        }

        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await SeedShopWithLegacyCounterAsync(first);
        await SeedShopWithLegacyCounterAsync(second);

        await using (var context = CreateMigratorContext())
        {
            await context.Database.MigrateAsync();
        }

        await using var connection = new NpgsqlConnection(MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select s.id, q.series_code, q.seller_registration_key, q.prefix, q.next_number, s.tax_regime, s.invoice_series_code
            from invoice_sequences q
            join shops s on s.id = q.shop_id
            order by s.id
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();

        var rows = new List<(Guid Shop, string Series, string Key, string Prefix, int Next, int Regime, string ShopSeries)>();
        while (await reader.ReadAsync())
            rows.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetString(6)));

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(string.Empty, row.Series);
            Assert.Equal($"legacy:{row.Shop}", row.Key);
            Assert.Equal("INV-2026-27-", row.Prefix);
            Assert.Equal(201, row.Next);
            Assert.Equal(0, row.Regime);
            Assert.Equal("A", row.ShopSeries);
        });
        Assert.Equal(new[] { first, second }.Order(), rows.Select(r => r.Shop));
    }

    private ApplicationDbContext CreateMigratorContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(MigratorConnectionString)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options);

    /// <summary>A shop and the offline counter its first lease opened, written the way the old code wrote them.</summary>
    private async Task SeedShopWithLegacyCounterAsync(Guid shopId)
    {
        await using var connection = new NpgsqlConnection(MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select set_config('app.active_shop_id', @shop::text, false);

            insert into shops (id, name, address, city, state, pincode, created_at)
            values (@shop, 'Legacy Shop', '42 MG Road', 'Bengaluru', 'Karnataka', '560001', now());

            insert into invoice_sequences (id, shop_id, fiscal_year_start, next_number, prefix, created_at)
            values (@sequence, @shop, 2026, 201, 'INV-2026-27-', now());
            """;
        command.Parameters.AddWithValue("shop", shopId);
        command.Parameters.AddWithValue("sequence", Guid.NewGuid());
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
