using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
namespace Intelibill.Infrastructure.Repositories;
internal sealed class OfflineExpiryEvidenceRepository(ApplicationDbContext db) : IOfflineExpiryEvidenceRepository
{
    public Task<OfflineExpiryEvidence?> GetAsync(Guid shopId, Guid evidenceId, CancellationToken ct) =>
        db.OfflineExpiryEvidence.AsNoTracking().SingleOrDefaultAsync(x => x.ShopId == shopId && x.Id == evidenceId, ct);
    public Task<Guid?> GetBatchItemAsync(Guid shopId, Guid batchId, CancellationToken ct) => db.InventoryBatches.Where(x => x.ShopId == shopId && x.Id == batchId).Select(x => (Guid?)x.ItemId).SingleOrDefaultAsync(ct);
    public async Task AddAsync(OfflineExpiryEvidence evidence, CancellationToken ct) => await db.OfflineExpiryEvidence.AddAsync(evidence, ct);
    public async Task<IReadOnlyList<InventoryBatch>> GetCapturePageAsync(Guid shopId, Guid? after, int pageSize, CancellationToken ct) =>
        await db.InventoryBatches.AsNoTracking().Include(x => x.Item).Where(x => x.ShopId == shopId && !x.IsVoided && x.Quantity > 0 && x.Item.IsActive && (!after.HasValue || x.Id.CompareTo(after.Value) > 0))
            .OrderBy(x => x.Id).Take(pageSize).ToListAsync(ct);
    public async Task<bool> HasRetainedCatalogAsync(Guid shopId, long catalogVersion, CancellationToken ct)
    {
        var state = await db.ShopExpiryStates.AsNoTracking().SingleOrDefaultAsync(x => x.ShopId == shopId, ct);
        if (state is null || catalogVersion < 1 || catalogVersion > state.CatalogVersion) return false;
        var versions = db.ExpiryPolicyRevisions.Where(x => x.ShopId == shopId).Select(x => x.CatalogVersion)
            .Concat(db.BatchExpiryRevisions.Where(x => x.ShopId == shopId).Select(x => x.CatalogVersion));
        // Union count detects a lost revision, including date-only invalidations omitted by policy maxima.
        return await versions.Where(x => x >= 1 && x <= state.CatalogVersion).Distinct().LongCountAsync(ct) == state.CatalogVersion;
    }
    // Historical effective-policy equality checks default revisions, while an unaffected item override
    // tolerates them. Item override/reset events and this lot's date edits remain relevant even if
    // the resulting settings happen to match their earlier values.
    public async Task<bool> HasRelevantChangeAsync(Guid shopId, Guid itemId, Guid batchId, long catalogVersion, DateTimeOffset asOf, CancellationToken ct) =>
        await db.ExpiryPolicyRevisions.AnyAsync(x => x.ShopId == shopId && x.ItemId == itemId && x.CatalogVersion > catalogVersion && x.EffectiveAt <= asOf, ct)
        || await db.BatchExpiryRevisions.AnyAsync(x => x.ShopId == shopId && x.InventoryBatchId == batchId && x.CatalogVersion > catalogVersion && x.EffectiveAt <= asOf, ct);
}
