using Intelibill.Shared.Web.Persistence;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
namespace Intelibill.Infrastructure.Repositories;
internal sealed class BatchDispositionRepository(ApplicationDbContext db) : IBatchDispositionRepository
{
    public async Task<InventoryBatch?> LockBatchAsync(Guid shopId, Guid batchId, CancellationToken ct) =>
        (await LockStockRowsAsync(shopId, [batchId], ct)).SingleOrDefault();

    public async Task LockBatchesAsync(Guid shopId, IReadOnlyCollection<Guid> batchIds, CancellationToken ct) =>
        await LockStockRowsAsync(shopId, batchIds, ct);

    /// <summary>
    /// Locks the item total rows of the batches, then the batch rows, each set in key order.
    /// </summary>
    /// <remarks>
    /// That is the order every writer without a lock of its own takes them in: a sale, count or
    /// adjustment saves through EF Core, which writes <c>inventory</c> before
    /// <c>inventory_batches</c> and each table's rows in key order. Locking the batch first would
    /// let such a writer hold the item total and wait for the batch while this holds the batch and
    /// waits for the total, and PostgreSQL would end one of them as a deadlock.
    /// </remarks>
    private async Task<IReadOnlyList<InventoryBatch>> LockStockRowsAsync(Guid shopId, IReadOnlyCollection<Guid> batchIds, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Disposition requires the source transaction.");
        var previousTimeout = (await db.Database.SqlQuery<string>($"SELECT current_setting('lock_timeout') AS \"Value\"").ToListAsync(ct)).Single();
        await db.SetLockTimeoutAsync(LockWait.Standard, ct);

        // Read without locking: a batch never moves to another item, and an item has one total row.
        var wanted = batchIds.Distinct().Order().ToArray();
        var itemIds = await db.InventoryBatches.AsNoTracking().Where(x => x.ShopId == shopId && wanted.Contains(x.Id)).Select(x => x.ItemId).Distinct().ToListAsync(ct);
        var stockIds = await db.Inventory.AsNoTracking().Where(x => x.ShopId == shopId && itemIds.Contains(x.ItemId)).Select(x => x.Id).ToListAsync(ct);

        db.ChangeTracker.DetectChanges();
        foreach (var stockId in stockIds.Order())
        {
            var stockEntry = db.ChangeTracker.Entries<Inventory>().SingleOrDefault(x => x.Entity.Id == stockId);
            var stocks = await db.Inventory.FromSqlInterpolated($"SELECT *, xmin FROM inventory WHERE shop_id = {shopId} AND id = {stockId} FOR NO KEY UPDATE").ToListAsync(ct);
            if (stockEntry is not null && stockEntry.State != EntityState.Modified && stocks.Count > 0) await stockEntry.ReloadAsync(ct);
        }

        var locked = new List<InventoryBatch>(wanted.Length);
        foreach (var batchId in wanted)
        {
            var tracked = db.ChangeTracker.Entries<InventoryBatch>().SingleOrDefault(x => x.Entity.Id == batchId);
            var changed = tracked?.State == EntityState.Modified;
            var rows = await db.InventoryBatches.FromSqlInterpolated($"SELECT *, xmin FROM inventory_batches WHERE shop_id = {shopId} AND id = {batchId} FOR NO KEY UPDATE").ToListAsync(ct);
            var batch = rows.SingleOrDefault();
            if (batch is not null && tracked is not null && !changed) await db.Entry(batch).ReloadAsync(ct);
            if (batch is not null) locked.Add(batch);
        }

        await db.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {previousTimeout}, true)", ct);
        return locked;
    }
    public Task<BatchDispositionChange?> GetRootAsync(Guid shopId, Guid batchId, Guid rootId, CancellationToken ct) =>
        db.BatchDispositionChanges.AsNoTracking().SingleOrDefaultAsync(x => x.ShopId == shopId && x.InventoryBatchId == batchId && x.Id == rootId && x.RootChangeId == null, ct);
    public async Task<decimal> GetRemainingAsync(Guid shopId, Guid rootId, CancellationToken ct)
    {
        var entries = await db.BatchDispositionChanges.AsNoTracking().Where(x => x.ShopId == shopId && (x.Id == rootId || x.RootChangeId == rootId)).ToListAsync(ct);
        entries.AddRange(db.BatchDispositionChanges.Local.Where(x => db.Entry(x).State == EntityState.Added && x.ShopId == shopId && (x.Id == rootId || x.RootChangeId == rootId)));
        return entries.Sum(x => x.RootChangeId == null ? x.Quantity : -x.Quantity);
    }
    public Task<bool> HasDateCorrectionAsync(Guid shopId, Guid batchId, DateOnly expiryDate, DateTimeOffset after, CancellationToken ct) =>
        db.BatchExpiryRevisions.AnyAsync(x => x.ShopId == shopId && x.InventoryBatchId == batchId && x.EffectiveAt >= after && x.NewExpiryDate == expiryDate && x.PreviousExpiryDate != x.NewExpiryDate, ct);
    public Task<bool> HasHistoryAsync(Guid shopId, Guid batchId, CancellationToken ct) => db.BatchDispositionChanges.AnyAsync(x => x.ShopId == shopId && x.InventoryBatchId == batchId, ct);
    public async Task<IReadOnlyList<BatchDispositionChange>> GetHistoryAsync(Guid shopId, Guid batchId, CancellationToken ct) =>
        await db.BatchDispositionChanges.AsNoTracking().Where(x => x.ShopId == shopId && x.InventoryBatchId == batchId).OrderBy(x => x.RecordedAt).ThenBy(x => x.Id).ToListAsync(ct);
    public async Task AddAsync(BatchDispositionChange change, CancellationToken ct) => await db.BatchDispositionChanges.AddAsync(change, ct);
}
