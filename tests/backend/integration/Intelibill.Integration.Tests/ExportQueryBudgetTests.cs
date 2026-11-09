using System.Data.Common;
using Intelibill.Application.Features.Exports.Sales;
using Intelibill.Application.Features.Exports.Sales.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Services.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed partial class ExportQueryBudgetTests(PostgreSqlTestFixture fixture, Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly ExportWindow June = new(new(2026, 6, 1), new(2026, 6, 30));
    private static readonly DateTimeOffset SoldAt = new(2026, 6, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LineCountBoundary_UsesSqlPreflight_AndCapsEveryDetailQuery()
    {
        var seed = await SeedAsync(2000);
        var commands = new Commands();
        await using var db = await RuntimeAsync(seed.Shop.Id, seed.User.Id, commands);
        await using var snapshot = await new ExportReadStore(db).OpenSnapshotAsync(CancellationToken.None);
        var counts = await snapshot.CountAsync(seed.Shop.Id, June, "lineItems", "pdf", CancellationToken.None);
        Assert.Equal(new ExportCounts(2000, 2001), counts);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.All(commands.Sql, sql => Assert.DoesNotContain("SELECT s.id,", sql, StringComparison.OrdinalIgnoreCase));
        commands.Sql.Clear();
        var dataset = await new SalesExportDatasetBuilder().BuildAsync(snapshot, seed.Shop, seed.User, June.From, June.To, "lineItems", CancellationToken.None);
        Assert.Equal(2000, dataset.LineItemRows.Count);
        Assert.Equal(2000m, dataset.SummaryRows.Single().TotalAmount);
        Assert.Equal(2000m, dataset.TaxBreakup.Single().SaleTaxableAmount);
        Assert.All(commands.Sql.Where(s => s.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)), sql => Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task AdjustmentOnlyPeriod_CountsReturnAndVoidSeparately_AndReadsOriginalSale()
    {
        var seed = await SeedAsync(1, outsideWindow: true);
        var returned = await AddReturnAsync(seed, voidInJune: true);
        await using var db = await RuntimeAsync(seed.Shop.Id, seed.User.Id);
        await using var snapshot = await new ExportReadStore(db).OpenSnapshotAsync(CancellationToken.None);
        Assert.Equal(new ExportCounts(2, 5), await snapshot.CountAsync(seed.Shop.Id, June, "summary", "pdf", CancellationToken.None));
        Assert.Equal(4, (await snapshot.CountAsync(seed.Shop.Id, June, "lineItems", "tallyXml", CancellationToken.None)).OutputRows);
        var data = await new SalesExportDatasetBuilder().BuildAsync(snapshot, seed.Shop, seed.User, June.From, June.To, "summary", CancellationToken.None);
        Assert.Empty(data.SummaryRows);
        Assert.Equal(2, data.ReturnAdjustments.Count);
        Assert.All(data.ReturnAdjustments, r => Assert.Equal(returned.Id, r.SaleReturnId));
        Assert.Equal(new[] { 1m, -1m }, data.ReturnAdjustments.Select(r => r.FinancialAdjustment));
        Assert.Equal(0m, data.TaxBreakup.Sum(t => t.ReturnTaxableAmount));
    }

    [Fact]
    public async Task HistoricalOriginalWithHugeLines_IsCountedBeforeAnyDetailRead()
    {
        var seed = await SeedAsync(1, outsideWindow: true);
        await AddReturnAsync(seed);
        await using (var owner = Owner())
        {
            // Materialize the composite once per copy; expanding (function()).* repeats it per column.
            await owner.Database.ExecuteSqlAsync($"""
                WITH copies AS MATERIALIZED (
                    SELECT jsonb_populate_record(NULL::sale_items,
                        to_jsonb(source) || jsonb_build_object('id', gen_random_uuid())) AS line
                    FROM sale_items source CROSS JOIN generate_series(1, 50000)
                    WHERE source.sale_id = {seed.Sale.Id}
                )
                INSERT INTO sale_items SELECT (line).* FROM copies
                """);
        }
        var commands = new Commands();
        await using var db = await RuntimeAsync(seed.Shop.Id, seed.User.Id, commands);
        await using var snapshot = await new ExportReadStore(db).OpenSnapshotAsync(CancellationToken.None);
        var count = await snapshot.CountAsync(seed.Shop.Id, June, "summary", "xlsx", CancellationToken.None);
        Assert.Equal(2, count.OutputRows); // one earlier-sales aggregate and one return posting
        Assert.Equal(50005, count.SourceRows);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.All(commands.Sql.Where(s => s.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)), sql =>
            Assert.True(sql.Contains("count(", StringComparison.OrdinalIgnoreCase) || sql.Contains("EXISTS", StringComparison.OrdinalIgnoreCase), sql));
    }

    [Fact]
    public async Task Snapshot_FreezesLaterInsertAndVoid_AndRlsHidesOtherShop()
    {
        var seed = await SeedAsync(1, outsideWindow: true);
        var returned = await AddReturnAsync(seed);
        var other = await SeedAsync(1);
        await using var db = await RuntimeAsync(seed.Shop.Id, seed.User.Id);
        await using (var snapshot = await new ExportReadStore(db).OpenSnapshotAsync(CancellationToken.None))
        {
            Assert.Equal(1, (await snapshot.CountAsync(seed.Shop.Id, June, "summary", "pdf", CancellationToken.None)).OutputRows);
            await using (var owner = Owner())
            {
                var fact = await owner.SaleReturns.SingleAsync(r => r.Id == returned.Id);
                fact.Void(SoldAt.AddDays(4), new(2026, 6, 14), seed.User.Id, "Duplicate entry");
                await owner.SaveChangesAsync();
            }
            var data = await new SalesExportDatasetBuilder().BuildAsync(snapshot, seed.Shop, seed.User, June.From, June.To, "summary", CancellationToken.None);
            Assert.Single(data.ReturnAdjustments);
            Assert.Equal(0, (await snapshot.CountAsync(other.Shop.Id, June, "summary", "pdf", CancellationToken.None)).OutputRows);
            await Assert.ThrowsAsync<InvalidOperationException>(() => new ExportReadStore(db).OpenSnapshotAsync(CancellationToken.None));
            var readOnly = await db.Database.SqlQueryRaw<string>("SHOW transaction_read_only").ToListAsync();
            Assert.Equal("on", readOnly.Single());
        }
        Assert.Null(db.Database.CurrentTransaction);
        await using var fresh = await new ExportReadStore(db).OpenSnapshotAsync(CancellationToken.None);
        Assert.Equal(2, (await fresh.CountAsync(seed.Shop.Id, June, "summary", "pdf", CancellationToken.None)).OutputRows);
    }

    [Fact]
    public async Task PhysicalReturnLabels_UseCurrentHistory_AndGroupedSqlQuantities()
    {
        var seed = await SeedAsync(1);
        await AddReturnAsync(seed, processedAt: SoldAt.AddMonths(1));
        var commands = new Commands();
        await using var db = await RuntimeAsync(seed.Shop.Id, seed.User.Id, commands);
        await using var snapshot = await new ExportReadStore(db).OpenSnapshotAsync(CancellationToken.None);
        Assert.Equal(new ExportCounts(1, 4), await snapshot.CountAsync(seed.Shop.Id, June, "lineItems", "pdf", CancellationToken.None));
        commands.Sql.Clear();
        var data = await new SalesExportDatasetBuilder().BuildAsync(snapshot, seed.Shop, seed.User, June.From, June.To, "lineItems", CancellationToken.None);
        Assert.Empty(data.ReturnAdjustments);
        var line = Assert.Single(data.LineItemRows);
        Assert.Equal(1m, line.ReturnedQuantity);
        Assert.Equal("FullyReturned", line.ReturnStatus);
        Assert.Equal("RET-001", line.ReturnNumbers);
        Assert.Contains(commands.Sql, sql => sql.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase) && sql.Contains("sum(", StringComparison.OrdinalIgnoreCase));
    }

    private ApplicationDbContext Owner() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(fixture.MigratorConnectionString).UseSnakeCaseNamingConvention().Options);

    private async Task<ApplicationDbContext> RuntimeAsync(Guid shop, Guid user, DbCommandInterceptor? commands = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(fixture.RuntimeConnectionString).UseSnakeCaseNamingConvention();
        if (commands is not null) options.AddInterceptors(commands);
        var db = new ApplicationDbContext(options.Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlAsync($"SELECT set_config('app.active_shop_id', {shop.ToString()}, false), set_config('app.current_user_id', {user.ToString()}, false)");
        return db;
    }

    private async Task<Seed> SeedAsync(int lines, bool outsideWindow = false)
    {
        await using var db = Owner();
        var user = User.CreateWithEmail($"export-{Guid.NewGuid():N}@example.com", "hash", "Export", "User");
        var shop = Shop.Create("Export test", "Address", "City", "State", "560001", null, null, null);
        var service = Service.Create(shop.Id, "SVC", "Frozen service", null, 1m, null, 0, false, true, user.Id);
        var items = Enumerable.Range(0, lines).Select(i => SaleItem.CreateService(shop.Id, service.Id, $"Frozen service {i}", "SVC", 1, 0, 1, 1, 0, false, false)).ToArray();
        var sale = Sale.Create(shop.Id, "INV-001", null, "Buyer", null, PaymentMethod.Cash, outsideWindow ? SoldAt.AddMonths(-1) : SoldAt, lines, 0, lines, 0, items);
        db.AddRange(user, shop, service, sale, ShopMembership.Create(shop.Id, user.Id, ShopRole.Owner, true));
        await db.SaveChangesAsync();
        return new(shop, user, sale);
    }

    private async Task<SaleReturn> AddReturnAsync(Seed seed, bool voidInJune = false, DateTimeOffset? processedAt = null)
    {
        await using var db = Owner();
        var line = seed.Sale.Items[0];
        var returned = SaleReturn.Record(seed.Shop.Id, seed.Sale.Id, "RET-001", processedAt ?? SoldAt.AddDays(1), BusinessDay.InDefaultZone(processedAt ?? SoldAt.AddDays(1)), seed.User.Id, null,
            1, 0, 1, ReturnPayoutDestination.Refund, 1, 0, null, null,
            [new(seed.Shop.Id, line.Id, 1, SaleReturnCondition.Restockable, 0, 1, 0, false, 1, 1, 1, 0, null)]).Value;
        Assert.False(returned.PostAccounting(ReturnAccountingSnapshot.Create("proportional-line-tax/1", [new(line.Id, 1, 0, 0)], [new(ReturnSettlementKind.ExternalRefund, 1)]).Value).IsError);
        if (voidInJune) returned.Void(SoldAt.AddDays(4), new(2026, 6, 14), seed.User.Id, "Duplicate entry");
        db.SaleReturns.Add(returned);
        await db.SaveChangesAsync();
        return returned;
    }

    private sealed record Seed(Shop Shop, User User, Sale Sale);

    private sealed class Commands : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Sql.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
