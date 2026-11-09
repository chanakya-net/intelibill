using System.Diagnostics;
using Intelibill.Application.Features.Exports.Sales.DTOs;
using Intelibill.Application.Features.Exports.Sales.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Services.Exports;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class ExportQueryBudgetTests
{
    [Theory]
    [InlineData(100, 26)]
    [InlineData(1000, 36)]
    public async Task Export_QueryCountScalesByPage(int count, int commandBudget)
    {
        var seed = await SeedSalesAsync(count, withReturns: true);
        var probe = new ReadQueryProbe();
        await using var db = await RuntimeAsync(seed.Shop.Id, seed.User.Id, probe);
        using var observed = probe.Observe();
        using var memory = new ReadMemoryObservation();
        var before = GC.GetTotalAllocatedBytes();
        var watch = Stopwatch.StartNew();
        SalesExportDatasetDto data;
        await using (var snapshot = await new ExportReadStore(db).OpenSnapshotAsync(CancellationToken.None))
        {
            Assert.Equal(new ExportCounts(count * 2L, count * 5L), await snapshot.CountAsync(seed.Shop.Id, June, "lineItems", "xlsx", CancellationToken.None));
            data = await BuildAsync(snapshot, seed);
        }
        // Reference is independent of the export projector: each invoice sold one unit for 1, fully returned for 1.
        Assert.Equal(count, data.SummaryRows.Count);
        Assert.Equal(count, data.LineItemRows.Count);
        Assert.Equal(count, data.ReturnAdjustments.Count);
        Assert.Equal(count, data.SummaryRows.Select(r => r.InvoiceNumber).Distinct().Count());
        Assert.All(data.SummaryRows, r => { Assert.Equal(1m, r.TotalAmount); Assert.Equal(1m, r.ReturnAmount); Assert.Equal(0m, r.NetSalesAmount); });
        Assert.All(data.LineItemRows, r => { Assert.Equal(1m, r.ReturnedQuantity); Assert.Equal("FullyReturned", r.ReturnStatus); });
        Assert.Equal(count, data.ReturnAdjustments.Sum(r => r.FinancialAdjustment));
        Assert.Equal(count, data.TaxBreakup.Sum(t => t.SaleTaxableAmount));
        Assert.Equal(count, data.TaxBreakup.Sum(t => t.ReturnTaxableAmount));
        Assert.Equal(commandBudget, observed.Reads.Count);
        AssertMaterializedPages(observed);
        Assert.Empty(db.ChangeTracker.Entries());
        output.WriteLine($"export sales={count}, commands={observed.Reads.Count}/{commandBudget}, actualRows={observed.Reads.Sum(r => r.Rows)}, maxReaderRows={observed.Reads.Max(r => r.Rows)}, elapsedMs={watch.ElapsedMilliseconds}, allocatedBytes={GC.GetTotalAllocatedBytes() - before}, sampledPeakManagedBytes={memory.PeakManagedBytes}, sampledPeakResidentBytes={memory.PeakResidentBytes}");
    }

    [Fact]
    public async Task SameTimestampPages_NoDuplicates()
    {
        var seed = await SeedSalesAsync(1001, withReturns: true);
        var nested = await SeedAsync(2001);
        var probe = new ReadQueryProbe();
        await using var db = await RuntimeAsync(seed.Shop.Id, seed.User.Id, probe);
        using var observed = probe.Observe();
        await using (var snapshot = await new ExportReadStore(db).OpenSnapshotAsync(CancellationToken.None))
        {
            var data = await BuildAsync(snapshot, seed);
            Assert.Equal(1001, data.SummaryRows.Select(r => r.InvoiceNumber).Distinct().Count());
            Assert.Equal(1001, data.ReturnAdjustments.Select(r => r.SaleReturnId).Distinct().Count());
            Assert.Equal(1001, data.LineItemRows.Count);
            Assert.Equal(1001m, data.ReturnAdjustments.Sum(r => r.FinancialAdjustment));
            await using var owner = Owner();
            var expected = await owner.Sales.Where(s => s.ShopId == seed.Shop.Id).OrderByDescending(s => s.SoldAt).ThenByDescending(s => s.Id).Select(s => s.InvoiceNumber).ToArrayAsync();
            Assert.Equal(expected, data.SummaryRows.Select(r => r.InvoiceNumber));
        }
        await using var nestedDb = await RuntimeAsync(nested.Shop.Id, nested.User.Id, probe);
        await using (var snapshot = await new ExportReadStore(nestedDb).OpenSnapshotAsync(CancellationToken.None))
        {
            // One parent crosses three child keyset pages; unique frozen names expose missing/duplicate UUID ties.
            var data = await BuildAsync(snapshot, nested);
            Assert.Equal(2001, data.LineItemRows.Count);
            Assert.Equal(2001, data.LineItemRows.Select(r => r.ItemName).Distinct().Count());
        }
        AssertMaterializedPages(observed);
    }

    [Fact]
    public async Task SameTimestampCancellationPages_NoDuplicates()
    {
        var seed = await SeedAsync(1);
        var goods = await AddGoodsSalesAsync(seed, 1001);
        await using (var owner = Owner())
        {
            foreach (var sale in goods) await CancelGoodsAsync(owner, seed, sale);
            await owner.SaveChangesAsync();
        }
        var probe = new ReadQueryProbe();
        await using var db = await RuntimeAsync(seed.Shop.Id, seed.User.Id, probe);
        using var observed = probe.Observe();
        await using var snapshot = await new ExportReadStore(db).OpenSnapshotAsync(CancellationToken.None);
        var data = await BuildAsync(snapshot, seed);
        Assert.Equal(1001, data.CancellationAdjustments.Count);
        Assert.Equal(1001, data.CancellationAdjustments.Select(c => c.CancellationId).Distinct().Count());
        Assert.Equal(1001, data.CancellationAdjustments.SelectMany(c => c.Lines).Select(l => l.Id).Distinct().Count());
        Assert.Equal(200200m, data.CancellationAdjustments.Sum(c => c.RevenueAmount));
        Assert.Equal(120120m, data.CancellationAdjustments.Sum(c => c.RecordedCogs));
        await using var readOwner = Owner();
        var expected = await readOwner.SaleCancellations.Where(c => c.ShopId == seed.Shop.Id).OrderBy(c => c.CancelledAt).ThenBy(c => c.Id).Select(c => c.Id).ToArrayAsync();
        Assert.Equal(expected, data.CancellationAdjustments.Select(c => c.CancellationId));
        AssertMaterializedPages(observed);
    }

    [Fact]
    public async Task ConcurrentInsert_DoesNotShiftSnapshot()
    {
        var seed = await SeedSalesAsync(1001);
        var probe = new ReadQueryProbe();
        await using var db = await RuntimeAsync(seed.Shop.Id, seed.User.Id, probe);
        using var observed = probe.Observe();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        observed.AfterRead = async read =>
        {
            if (read.Sql.Contains("FROM sales AS", StringComparison.Ordinal) && !read.IsAggregate && read.Rows == 1000)
            { reached.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
        };
        await using (var snapshot = await new ExportReadStore(db).OpenSnapshotAsync(CancellationToken.None))
        {
            await snapshot.CountAsync(seed.Shop.Id, June, "lineItems", "xlsx", CancellationToken.None);
            var pending = BuildAsync(snapshot, seed);
            try
            {
                await reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
                await using var owner = Owner();
                owner.Sales.Add(Sale.Create(seed.Shop.Id, "CONCURRENT", null, "Concurrent buyer", null, PaymentMethod.Cash, SoldAt.AddDays(1), 0, 0, 0, 0, []));
                await owner.SaveChangesAsync();
            }
            finally { release.TrySetResult(); }
            var frozen = await pending;
            Assert.Equal(1001, frozen.SummaryRows.Count);
            Assert.DoesNotContain(frozen.SummaryRows, r => r.InvoiceNumber == "CONCURRENT");
        }
        observed.AfterRead = null;
        await using var fresh = await new ExportReadStore(db).OpenSnapshotAsync(CancellationToken.None);
        var latest = await BuildAsync(fresh, seed);
        Assert.Equal(1002, latest.SummaryRows.Count);
        Assert.Contains(latest.SummaryRows, r => r.InvoiceNumber == "CONCURRENT");
        AssertMaterializedPages(observed);
    }

    [Fact]
    public async Task ConcurrentReturnVoidAndCancellation_StayInSnapshot()
    {
        var seed = await SeedSalesAsync(2, withReturns: true);
        var goods = await AddGoodsSaleAsync(seed);
        var probe = new ReadQueryProbe();
        await using var db = await RuntimeAsync(seed.Shop.Id, seed.User.Id, probe);
        using var observed = probe.Observe();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = 0;
        observed.AfterRead = async read =>
        {
            if (read.IsAggregate && Interlocked.Increment(ref first) == 1)
            { reached.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
        };
        await using (var snapshot = await new ExportReadStore(db).OpenSnapshotAsync(CancellationToken.None))
        {
            var pending = snapshot.CountAsync(seed.Shop.Id, June, "lineItems", "xlsx", CancellationToken.None);
            try
            {
                await reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
                await using var owner = Owner();
                var returned = await owner.SaleReturns.SingleAsync(r => r.SaleId == seed.Sale.Id);
                returned.Void(SoldAt.AddDays(4), new(2026, 6, 14), seed.User.Id, "Concurrent void");
                await CancelGoodsAsync(owner, seed, goods);
                await owner.SaveChangesAsync();
            }
            finally { release.TrySetResult(); }
            var frozenCount = await pending;
            Assert.Equal(5, frozenCount.OutputRows);
            observed.AfterRead = null;
            var frozen = await BuildAsync(snapshot, seed);
            Assert.Equal(2, frozen.ReturnAdjustments.Count);
            Assert.Empty(frozen.CancellationAdjustments);
            Assert.All(frozen.SummaryRows, r => Assert.Equal("Posted", r.Status));
            Assert.All(frozen.LineItemRows.Where(r => r.InvoiceNumber != goods.InvoiceNumber), r => Assert.Equal(1m, r.ReturnedQuantity));
        }
        await using var fresh = await new ExportReadStore(db).OpenSnapshotAsync(CancellationToken.None);
        Assert.Equal(8, (await fresh.CountAsync(seed.Shop.Id, June, "lineItems", "xlsx", CancellationToken.None)).OutputRows);
        var latest = await BuildAsync(fresh, seed);
        Assert.Equal(3, latest.ReturnAdjustments.Count);
        Assert.Equal(1m, latest.ReturnAdjustments.Sum(r => r.FinancialAdjustment));
        Assert.Single(latest.CancellationAdjustments);
        Assert.Equal("Cancelled", latest.SummaryRows.Single(r => r.InvoiceNumber == goods.InvoiceNumber).Status);
        Assert.Equal(0m, latest.LineItemRows.Single(r => r.InvoiceNumber == seed.Sale.InvoiceNumber).ReturnedQuantity);
    }

    [Fact]
    public async Task CanceledExport_StopsFurtherPages()
    {
        var seed = await SeedSalesAsync(1001);
        var probe = new ReadQueryProbe();
        await using var db = await RuntimeAsync(seed.Shop.Id, seed.User.Id, probe);
        using var observed = probe.Observe();
        using var cancellation = new CancellationTokenSource();
        observed.AfterRead = read => { if (!read.IsAggregate && read.Rows == 1000) cancellation.Cancel(); return Task.CompletedTask; };
        await using (var snapshot = await new ExportReadStore(db).OpenSnapshotAsync(CancellationToken.None))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BuildAsync(snapshot, seed, cancellation.Token));
            Assert.Single(observed.Reads);
            Assert.Equal(1000, observed.Reads.Single().Rows);
        }
        Assert.Null(db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task OtherShopRows_NeverAppear()
    {
        var seed = await SeedSalesAsync(2, withReturns: true);
        var foreign = await SeedSalesAsync(3, withReturns: true);
        var foreignGoods = await AddGoodsSaleAsync(foreign);
        await using (var owner = Owner()) { await CancelGoodsAsync(owner, foreign, foreignGoods); await owner.SaveChangesAsync(); }
        await using var db = await RuntimeAsync(seed.Shop.Id, seed.User.Id);
        await using var snapshot = await new ExportReadStore(db).OpenSnapshotAsync(CancellationToken.None);
        var own = await BuildAsync(snapshot, seed);
        Assert.Equal(2, own.SummaryRows.Count);
        Assert.Equal(2, own.ReturnAdjustments.Count);
        Assert.Empty(own.CancellationAdjustments);
        // Deliberately pass the foreign ID as the explicit predicate too: runtime RLS remains decisive.
        var hidden = await BuildAsync(snapshot, foreign);
        Assert.Empty(hidden.SummaryRows);
        Assert.Empty(hidden.LineItemRows);
        Assert.Empty(hidden.ReturnAdjustments);
        Assert.Empty(hidden.CancellationAdjustments);
    }

    [Fact]
    public async Task AllMaterializedReads_AreCapped_NearLimitXlsx()
    {
        var seed = await SeedAsync(1);
        await using (var owner = Owner())
        {
            // Keep JSON conversion outside composite expansion so it runs once per generated line.
            await owner.Database.ExecuteSqlAsync($"""
                WITH copies AS MATERIALIZED (
                    SELECT jsonb_populate_record(NULL::sale_items,
                        to_jsonb(source) || jsonb_build_object('id', gen_random_uuid(), 'line_name', 'Line ' || g::text)) AS line
                    FROM sale_items source CROSS JOIN generate_series(1, 49998) g WHERE source.sale_id = {seed.Sale.Id}
                )
                INSERT INTO sale_items SELECT (line).* FROM copies
                """);
            await owner.Database.ExecuteSqlAsync($"UPDATE sales SET total_before_discount = 49999, total_amount = 49999, paid_amount = 49999 WHERE id = {seed.Sale.Id}");
        }
        var probe = new ReadQueryProbe();
        await using var db = await RuntimeAsync(seed.Shop.Id, seed.User.Id, probe);
        using var observed = probe.Observe();
        using var memory = new ReadMemoryObservation();
        var before = GC.GetTotalAllocatedBytes();
        var watch = Stopwatch.StartNew();
        SalesExportDatasetDto data;
        await using (var snapshot = await new ExportReadStore(db).OpenSnapshotAsync(CancellationToken.None))
        {
            Assert.Equal(new ExportCounts(49999, 50000), await snapshot.CountAsync(seed.Shop.Id, June, "lineItems", "xlsx", CancellationToken.None));
            data = await BuildAsync(snapshot, seed);
        }
        Assert.Null(db.Database.CurrentTransaction);
        Assert.Equal(49999, data.LineItemRows.Count);
        Assert.Equal(49999m, data.LineItemRows.Sum(l => l.LineTotal));
        Assert.Equal(49999m, Assert.Single(data.SummaryRows).TotalAmount);
        Assert.Equal(49999, data.LineItemRows.Select(l => l.ItemName).Distinct().Count());
        AssertMaterializedPages(observed);
        var materializeMs = watch.ElapsedMilliseconds;
        var rendered = await new SalesExcelExportRenderer().RenderAsync(data, CancellationToken.None);
        Assert.NotEmpty(rendered.Content);
        output.WriteLine($"near-limit xlsx outputRows=49999 sourceRows=50000 commands={observed.Reads.Count} actualRows={observed.Reads.Sum(r => r.Rows)} maxReaderRows={observed.Reads.Max(r => r.Rows)} materializeMs={materializeMs} totalMs={watch.ElapsedMilliseconds} allocatedBytes={GC.GetTotalAllocatedBytes() - before} sampledPeakManagedBytes={memory.PeakManagedBytes}, sampledPeakResidentBytes={memory.PeakResidentBytes} fileBytes={rendered.Content.Length}");
    }

    private static void AssertMaterializedPages(ReadQueryProbe.Observation observed)
    {
        Assert.NotEmpty(observed.Reads);
        Assert.All(observed.Reads, r => Assert.InRange(r.Rows, 0, 1000));
        Assert.All(observed.Reads.Where(r => !r.IsAggregate), r => Assert.Contains("LIMIT", r.Sql, StringComparison.OrdinalIgnoreCase));
    }

    private static Task<SalesExportDatasetDto> BuildAsync(IExportReadSnapshot snapshot, Seed seed, CancellationToken ct = default) =>
        new SalesExportDatasetBuilder().BuildAsync(snapshot, seed.Shop, seed.User, June.From, June.To, "lineItems", ct);

    private async Task<Seed> SeedSalesAsync(int count, bool withReturns = false)
    {
        var seed = await SeedAsync(1);
        await using var db = Owner();
        var firstLine = seed.Sale.Items[0];
        List<Sale> sales = [seed.Sale];
        for (var i = 1; i < count; i++)
        {
            var line = SaleItem.CreateService(seed.Shop.Id, firstLine.ServiceId!.Value, $"Frozen service {i}", "SVC", 1, 0, 1, 1, 0, false, false);
            var sale = Sale.Create(seed.Shop.Id, $"INV-{i + 1:00000}", null, "Buyer", null, PaymentMethod.Cash, SoldAt, 1, 0, 1, 0, [line]);
            db.Sales.Add(sale);
            sales.Add(sale);
        }
        if (withReturns)
        {
            foreach (var sale in sales)
            {
                var line = sale.Items[0];
                var returned = SaleReturn.Record(seed.Shop.Id, sale.Id, $"RET-{sale.InvoiceNumber}", SoldAt.AddDays(1), BusinessDay.InDefaultZone(SoldAt.AddDays(1)), seed.User.Id, null,
                    1, 0, 1, ReturnPayoutDestination.Refund, 1, 0, null, null,
                    [new(seed.Shop.Id, line.Id, 1, SaleReturnCondition.Restockable, 0, 1, 0, false, 1, 1, 1, 0, null)]).Value;
                Assert.False(returned.PostAccounting(ReturnAccountingSnapshot.Create("proportional-line-tax/1", [new(line.Id, 1, 0, 0)], [new(ReturnSettlementKind.ExternalRefund, 1)]).Value).IsError);
                db.SaleReturns.Add(returned);
            }
        }
        await db.SaveChangesAsync();
        return seed;
    }

    private async Task<Sale> AddGoodsSaleAsync(Seed seed) => (await AddGoodsSalesAsync(seed, 1))[0];

    private async Task<Sale[]> AddGoodsSalesAsync(Seed seed, int count)
    {
        await using var owner = Owner();
        var item = Item.Create(seed.Shop.Id, "Goods", null, "kg", Guid.NewGuid().ToString("N"), true, seed.User.Id);
        var batch = InventoryBatch.Create(seed.Shop.Id, item.Id, "LOT", count * 2, 60, 100, 100, 0, false, null, null, null, seed.User.Id).Value;
        var sales = Enumerable.Range(0, count).Select(i => Sale.Record(seed.Shop.Id, seed.User.Id, Guid.NewGuid().ToString("N"), "HASH", $"GOODS-{i}",
            [new SaleLineInput(seed.Shop.Id, SaleLineType.Goods, item.Id, batch.Id, null, "Goods", "GOODS", 2, 60, 100, 100, 0, false, false)],
            null, "Buyer", null, PaymentMethod.Cash, 200, 0, SoldAt, BusinessDay.InDefaultZone(SoldAt)).Value).ToArray();
        owner.AddRange(item, batch);
        owner.AddRange(sales);
        await owner.SaveChangesAsync();
        return sales;
    }

    private static async Task CancelGoodsAsync(Intelibill.Infrastructure.Data.ApplicationDbContext owner, Seed seed, Sale sale)
    {
        var line = sale.Items[0];
        var movement = StockTransaction.Create(seed.Shop.Id, line.ItemId!.Value, line.InventoryBatchId!.Value, StockTransactionType.SaleCancellation, 2, "SC", null, SoldAt.AddDays(2), seed.User.Id, seed.User.Id).Value;
        var cancellation = SaleCancellation.Record(new(seed.Shop.Id, sale.Id, seed.User.Id, Guid.NewGuid().ToString("N"), "Mistaken entry", SoldAt.AddDays(2),
            new(2026, 6, 12), "Asia/Kolkata", sale.InvoiceNumber, new(2026, 6, 10), 200, 200, 0, 0, 200, null,
            new("Seller", "Shop", "Address", "29", "Karnataka", null, TaxRegime.Unregistered),
            new("Buyer", null, null, null, null, null), true, true,
            [new(seed.Shop.Id, sale.Id, line.Id, line.ItemId.Value, line.InventoryBatchId.Value, movement.Id, 2, "Goods", "KGS", 60, 200, 120)])).Value;
        var persisted = await owner.Sales.SingleAsync(s => s.Id == sale.Id);
        Assert.False(persisted.MarkCancelled().IsError);
        owner.AddRange(movement, cancellation);
    }
}
