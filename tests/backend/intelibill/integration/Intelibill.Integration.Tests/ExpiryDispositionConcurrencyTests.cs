using Intelibill.Domain.Enums;
using System.Net;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class ExpiryDispositionConcurrencyTests(PostgreSqlTestFixture fixture) : ExpiryDispositionFixture(fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentDisposals_ConserveRootAndPhysicalStock(bool sameKey)
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop, quantity: 5m); var held = await RecoverAsync(shop, sale);
        var key = Guid.NewGuid().ToString();
        var responses = await Task.WhenAll(DisposeAsync(shop, sale.BatchId, held.Root, held.Batch.Version, key: key),
            DisposeAsync(shop, sale.BatchId, held.Root, held.Batch.Version, key: sameKey ? key : Guid.NewGuid().ToString()));
        try
        {
            Assert.Equal(sameKey ? 2 : 1, responses.Count(x => x.IsSuccessStatusCode));
            Assert.All(responses, x => Assert.Contains(x.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
            var batch = await BatchAsync(sale); Assert.Equal((5m,0m),(batch.Quantity,batch.QuarantinedQuantity));
            var changes = await QueryAsync(db => db.BatchDispositionChanges.Where(x => x.InventoryBatchId == batch.Id).ToListAsync());
            Assert.Equal(2, changes.Count); Assert.Equal(0, changes.Sum(x => x.RootChangeId is null ? x.Quantity : -x.Quantity));
            Assert.Equal(1, await QueryAsync(db => db.InventoryAdjustments.CountAsync(x => x.InventoryBatchId == batch.Id)));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task ConcurrentVoidAndDisposal_OneConsumesExactSource()
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop, quantity: 5m); var held = await RecoverAsync(shop, sale);
        var responses = await Task.WhenAll(DisposeAsync(shop, sale.BatchId, held.Root, held.Batch.Version),
            SendAsync(shop, HttpMethod.Post, $"/api/sales/returns/{held.Return}/void", new { reason = "Wrong return" }));
        try
        {
            Assert.Single(responses, x => x.IsSuccessStatusCode); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
            var batch = await BatchAsync(sale); Assert.Equal((5m,0m),(batch.Quantity,batch.QuarantinedQuantity));
            var changes = await QueryAsync(db => db.BatchDispositionChanges.Where(x => x.InventoryBatchId == batch.Id).ToListAsync());
            Assert.Equal(2, changes.Count); Assert.Equal(held.Root, changes.Single(x => x.RootChangeId.HasValue).RootChangeId);
            Assert.Equal(0, changes.Sum(x => x.RootChangeId is null ? x.Quantity : -x.Quantity));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }
    [Fact]
    public async Task ConcurrentSaleAndDisposal_NeverConsumeHeldUnitsOrLosePhysicalMovement()
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop, quantity: 5m); var held = await RecoverAsync(shop, sale);
        var b = held.Batch;
        var sell = SendAsync(shop, HttpMethod.Post, "/api/sales", new { idempotencyKey = Guid.NewGuid().ToString(), paymentMethod = 1, paidAmount = 590m, dueAmount = 0m,
            items = new[] { new { barcode = b.Item.Barcode, batchNumber = b.BatchNumber, itemName = b.Item.Name, quantity = 5m, costPrice = 80m, salesPrice = 100m,
                mrp = 120m, taxRatePercent = 18m, isPriceIncludingTax = false, inventoryBatchId = b.Id } } });
        var responses = await Task.WhenAll(sell, DisposeAsync(shop, b.Id, held.Root, b.Version));
        try
        {
            Assert.Contains(responses, x => x.IsSuccessStatusCode);
            Assert.All(responses, x => Assert.Contains(x.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.Conflict }));
            var current = await BatchAsync(sale);
            var roots = await QueryAsync(db => db.BatchDispositionChanges.Where(x => x.InventoryBatchId == b.Id).ToListAsync());
            var heldBalance = roots.Sum(x => x.RootChangeId is null ? x.Quantity : -x.Quantity);
            Assert.Equal(heldBalance, current.QuarantinedQuantity); Assert.True(current.Quantity >= heldBalance);
            var saleCommitted = responses[0].IsSuccessStatusCode; var disposed = responses[1].IsSuccessStatusCode;
            Assert.Equal(7m - (saleCommitted ? 5m : 0m) - (disposed ? 2m : 0m), current.Quantity);
            Assert.Equal(disposed ? 0m : 2m, current.QuarantinedQuantity);
            Assert.Equal(current.Quantity, await QueryAsync(db => db.Inventory.Where(x => x.ItemId == b.ItemId).Select(x => x.Quantity).SingleAsync()));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanRestorationOnPartiallyHeldBatch_PreservesRootsDuringCountOrDisposal(bool countInstead)
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop,quantity:5m); var held = await RecoverAsync(shop,sale);
        await CorrectDateAsync(shop,held.Batch); var b = await BatchAsync(sale);
        var returning = SendAsync(shop,HttpMethod.Post,$"/api/sales/{sale.SaleId}/returns",new { payoutDestination = (int)ReturnPayoutDestination.Refund,
            items = new[] { Line(sale.LineId,quantity:1m) } });
        var competing = countInstead
            ? SendAsync(shop,HttpMethod.Post,$"/api/inventory/batches/{b.Id}/count",new { expectedVersion = b.Version,countedQuantity = 5m,reason = "Count unheld goods",operationKey = Guid.NewGuid().ToString() })
            : DisposeAsync(shop,b.Id,held.Root,b.Version);
        var responses = await Task.WhenAll(returning,competing);
        try
        {
            Assert.Contains(responses,x => x.IsSuccessStatusCode);
            foreach (var response in responses) Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict,await response.Content.ReadAsStringAsync());
            var current = await BatchAsync(sale); var returned = responses[0].IsSuccessStatusCode; var competed = responses[1].IsSuccessStatusCode;
            Assert.Equal(7m + (returned?1m:0m) - (competed?2m:0m),current.Quantity);
            Assert.Equal(!countInstead && competed?0m:2m,current.QuarantinedQuantity);
            var entries = await QueryAsync(db => db.BatchDispositionChanges.Where(x => x.InventoryBatchId == b.Id).ToListAsync());
            Assert.Equal(current.QuarantinedQuantity,entries.Sum(x => x.RootChangeId is null?x.Quantity:-x.Quantity));
            Assert.Equal(current.Quantity,await QueryAsync(db => db.Inventory.Where(x => x.ItemId == b.ItemId).Select(x => x.Quantity).SingleAsync()));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    // ── Stock row lock order ──────────────────────────────────────────────────────────────────
    //
    // A sale takes no stock lock of its own. When it saves it writes the item's total row and then
    // the batch row. A disposal locks both rows before it writes. If it locked the batch first, a
    // sale between its two writes would hold the total and wait for the batch while the disposal
    // held the batch and waited for the total: a deadlock, which the sale answers with a 500.
    // The two tests below pin both halves. The other side is played by a plain session, so the
    // overlap happens every time instead of once in a while.

    [Fact]
    public async Task Sale_WritesTheItemTotalBeforeItsBatch()
    {
        using var shop = await OpenShopAsync(); var sold = await SellGoodsAsync(shop, quantity: 5m); var batch = await BatchAsync(sold);
        await using var other = await StockSession.OpenAsync(Factory.MigratorConnectionString);
        await other.ExecuteAsync("SELECT id FROM inventory_batches WHERE id = @id FOR NO KEY UPDATE", batch.Id);

        var sale = SellCurrentAsync(shop, batch, 1m);
        await other.WaitUntilBlockingAsync();

        // The sale is waiting for the batch. If it already holds the item total, the total cannot
        // be locked now.
        var refused = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
            other.ExecuteAsync("SELECT id FROM inventory WHERE item_id = @id FOR NO KEY UPDATE NOWAIT", batch.ItemId));
        Assert.Equal(Npgsql.PostgresErrorCodes.LockNotAvailable, refused.SqlState);

        await other.RollbackAsync();
        using var response = await sale;
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Disposal_BetweenASalesTwoWrites_LeavesTheBatchFreeAndConflicts()
    {
        using var shop = await OpenShopAsync(); var sold = await SellGoodsAsync(shop, quantity: 5m); var held = await RecoverAsync(shop, sold);
        await using var sale = await StockSession.OpenAsync(Factory.MigratorConnectionString);

        // The sale's first write: the item total.
        await sale.ExecuteAsync("UPDATE inventory SET quantity = quantity WHERE item_id = @id", held.Batch.ItemId);

        var disposal = DisposeAsync(shop, sold.BatchId, held.Root, held.Batch.Version);
        await sale.WaitUntilBlockingAsync();

        // The sale's second write: the batch. It may not wait on the disposal, which is itself
        // waiting on this session.
        await sale.ExecuteAsync("UPDATE inventory_batches SET quantity = quantity WHERE id = @id", sold.BatchId);
        await sale.CommitAsync();

        using var response = await disposal;
        var answer = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Conflict, $"{response.StatusCode} {answer}");
        Assert.Contains("Expiry.BatchVersionConflict", answer, StringComparison.Ordinal);
        var batch = await BatchAsync(sold);
        Assert.Equal(2m, batch.QuarantinedQuantity);
        Assert.Equal(0, await QueryAsync(db => db.InventoryAdjustments.CountAsync(x => x.InventoryBatchId == batch.Id)));
    }

    /// <summary>A plain database session standing in for another request's stock writes.</summary>
    private sealed class StockSession : IAsyncDisposable
    {
        private readonly Npgsql.NpgsqlConnection _session;
        private readonly Npgsql.NpgsqlConnection _observer;
        private readonly Npgsql.NpgsqlTransaction _transaction;

        private StockSession(Npgsql.NpgsqlConnection session, Npgsql.NpgsqlConnection observer, Npgsql.NpgsqlTransaction transaction) =>
            (_session, _observer, _transaction) = (session, observer, transaction);

        public static async Task<StockSession> OpenAsync(string connectionString)
        {
            var session = new Npgsql.NpgsqlConnection(connectionString);
            var observer = new Npgsql.NpgsqlConnection(connectionString);
            await session.OpenAsync();
            await observer.OpenAsync();
            return new StockSession(session, observer, await session.BeginTransactionAsync());
        }

        public async Task ExecuteAsync(string sql, Guid id)
        {
            await using var command = new Npgsql.NpgsqlCommand(sql, _session, _transaction);
            command.Parameters.AddWithValue("id", id);
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>Returns once another session is waiting for a lock this one holds.</summary>
        public async Task WaitUntilBlockingAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            while (true)
            {
                await using var command = new Npgsql.NpgsqlCommand(
                    "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' AND @blocker = ANY(pg_blocking_pids(pid))",
                    _observer);
                command.Parameters.AddWithValue("blocker", _session.ProcessID);
                if ((long)(await command.ExecuteScalarAsync(timeout.Token))! > 0) return;
                await Task.Delay(10, timeout.Token);
            }
        }

        public Task CommitAsync() => _transaction.CommitAsync();

        public Task RollbackAsync() => _transaction.RollbackAsync();

        public async ValueTask DisposeAsync()
        {
            await _transaction.DisposeAsync();
            await _session.DisposeAsync();
            await _observer.DisposeAsync();
        }
    }
}
