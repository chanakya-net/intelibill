using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
namespace Intelibill.Infrastructure.Repositories;
internal sealed class BatchDispositionRepository(ApplicationDbContext db) : IBatchDispositionRepository
{
    public async Task<InventoryBatch?> LockBatchAsync(Guid shopId, Guid batchId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Disposition requires the source transaction.");
        var previousTimeout = (await db.Database.SqlQuery<string>($"SELECT current_setting('lock_timeout') AS \"Value\"").ToListAsync(ct)).Single();
        await db.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {"5s"}, true)", ct);
        db.ChangeTracker.DetectChanges();
        var tracked = db.ChangeTracker.Entries<InventoryBatch>().SingleOrDefault(x => x.Entity.Id == batchId);
        var changed = tracked?.State == EntityState.Modified;
        var rows = await db.InventoryBatches.FromSqlInterpolated($"SELECT *, xmin FROM inventory_batches WHERE shop_id = {shopId} AND id = {batchId} FOR NO KEY UPDATE").ToListAsync(ct);
        var batch = rows.SingleOrDefault();
        if (batch is not null && tracked is not null && !changed) await db.Entry(batch).ReloadAsync(ct);
        if (batch is not null)
        {
            db.ChangeTracker.DetectChanges();
            var stockEntry = db.ChangeTracker.Entries<Inventory>().SingleOrDefault(x => x.Entity.ShopId == shopId && x.Entity.ItemId == batch.ItemId);
            var stocks = await db.Inventory.FromSqlInterpolated($"SELECT *, xmin FROM inventory WHERE shop_id = {shopId} AND item_id = {batch.ItemId} FOR NO KEY UPDATE").ToListAsync(ct);
            if (stockEntry is not null && stockEntry.State != EntityState.Modified && stocks.Count > 0) await stockEntry.ReloadAsync(ct);
        }
        await db.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {previousTimeout}, true)", ct);
        return batch;
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
