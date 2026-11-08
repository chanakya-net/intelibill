using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class ExpiryPolicyRepository(ApplicationDbContext db) : IExpiryPolicyRepository
{
    public async Task InitializeAsync(Guid shopId, Guid actor, DateTimeOffset now, CancellationToken ct)
    {
        await db.ShopExpiryStates.AddAsync(ShopExpiryState.Create(shopId, now), ct);
        await AddRevisionAsync(ExpiryPolicyRevision.Create(shopId, null, 1, ExpiryPolicy.Default, actor, "Initial compatible policy", now), ct);
    }

    public Task<ExpiryPolicyRevision?> GetInitialRevisionAsync(Guid shopId, CancellationToken ct) =>
        db.ExpiryPolicyRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.ShopId == shopId && x.CatalogVersion == 1, ct);

    public async Task<ShopExpiryState?> GetStateAsync(Guid shopId, CancellationToken ct)
    {
        var state = await db.ShopExpiryStates.SingleOrDefaultAsync(x => x.ShopId == shopId, ct);
        if (state is not null) await db.Entry(state).ReloadAsync(ct);
        return state;
    }
    public Task<bool> ItemExistsAsync(Guid shopId, Guid itemId, CancellationToken ct) =>
        db.Items.AnyAsync(x => x.ShopId == shopId && x.Id == itemId, ct);

    public Task<ExpiryPolicyRevision?> GetLatestRevisionAsync(Guid shopId, Guid? itemId, DateTimeOffset? asOf, CancellationToken ct)
    {
        var query = db.ExpiryPolicyRevisions.AsNoTracking().Where(x => x.ShopId == shopId && x.ItemId == itemId);
        if (asOf.HasValue) query = query.Where(x => x.EffectiveAt <= asOf.Value || x.PreFeatureBaseline);
        return query.OrderByDescending(x => x.CatalogVersion).FirstOrDefaultAsync(ct);
    }

    public async Task<EffectiveExpiryPolicy> GetEffectiveAsync(Guid shopId, Guid itemId, DateTimeOffset? asOf, CancellationToken ct)
    {
        var state = await db.ShopExpiryStates.AsNoTracking().SingleOrDefaultAsync(x => x.ShopId == shopId, ct);
        if (state is null || (asOf.HasValue && asOf.Value < state.InitializedAt) || !await ItemExistsAsync(shopId, itemId, ct)) return new(null, 0, false);
        var baseline = await db.ExpiryPolicyRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.ShopId == shopId && x.CatalogVersion == 1, ct);
        if (baseline is null || baseline.ItemId is not null || baseline.Policy != ExpiryPolicy.Default || baseline.PreFeatureBaseline != state.PreFeatureBaseline)
            return new(null, state.CatalogVersion, false);
        // The retained current default must agree with the state even for historical reads.
        // Otherwise a missing edit could turn its surviving baseline into false Ignore proof.
        var currentDefault = await GetLatestRevisionAsync(shopId, null, null, ct);
        if (currentDefault?.Policy is null || !currentDefault.Policy.IsValid() || currentDefault.CatalogVersion != state.DefaultPolicyVersion)
            return new(null, state.CatalogVersion, false);
        var defaults = asOf.HasValue ? await GetLatestRevisionAsync(shopId, null, asOf, ct) : currentDefault;
        if (defaults?.Policy is null || !defaults.Policy.IsValid()) return new(null, state.CatalogVersion, false);
        var item = await GetLatestRevisionAsync(shopId, itemId, asOf, ct);
        var policy = item?.Policy ?? defaults.Policy;
        if (!policy.IsValid()) return new(null, state.CatalogVersion, false);
        var version = asOf.HasValue
            ? await db.ExpiryPolicyRevisions.Where(x => x.ShopId == shopId && (x.EffectiveAt <= asOf.Value || x.PreFeatureBaseline)).MaxAsync(x => (long?)x.CatalogVersion, ct) ?? 0
            : state.CatalogVersion;
        if (version < policy.Version) return new(null, version, false);
        return new(policy, version, true, defaults.PreFeatureBaseline && item is null, item?.EffectiveAt ?? defaults.EffectiveAt);
    }

    public async Task AddRevisionAsync(ExpiryPolicyRevision revision, CancellationToken ct) => await db.ExpiryPolicyRevisions.AddAsync(revision, ct);

    public async Task<ExpiryReadinessPage> GetReadinessAsync(Guid shopId, DateOnly date, int page, int pageSize, CancellationToken ct)
    {
        var defaults = (await GetLatestRevisionAsync(shopId, null, null, ct))!.Policy!;
        var lots = db.InventoryBatches.AsNoTracking().Where(x => x.ShopId == shopId && !x.IsVoided && x.Quantity > 0)
            .Select(x => new
            {
                Batch = x,
                Inclusive = db.ExpiryPolicyRevisions.Where(r => r.ShopId == shopId && r.ItemId == x.ItemId)
                    .OrderByDescending(r => r.CatalogVersion).Select(r => r.SellThroughExpiryDate).FirstOrDefault() ?? defaults.SellThroughExpiryDate,
            })
            .Select(x => new
            {
                BatchId = x.Batch.Id, x.Batch.ItemId, ItemName = x.Batch.Item.Name, x.Batch.BatchNumber,
                x.Batch.ExpiryDate, PhysicalQuantity = x.Batch.Quantity, x.Batch.QuarantinedQuantity,
                MissingDate = x.Batch.ExpiryDate == null,
                Expired = x.Batch.ExpiryDate != null && (x.Inclusive ? date > x.Batch.ExpiryDate : date >= x.Batch.ExpiryDate),
            });
        var totals = await lots.GroupBy(x => 1).Select(g => new
        {
            Total = g.Count(x => x.MissingDate || x.Expired || x.QuarantinedQuantity > 0),
            Missing = g.Count(x => x.MissingDate), Expired = g.Count(x => x.Expired), Quarantined = g.Count(x => x.QuarantinedQuantity > 0),
            Physical = g.Sum(x => x.PhysicalQuantity), Held = g.Sum(x => x.QuarantinedQuantity),
        }).SingleOrDefaultAsync(ct);
        var items = await lots.Where(x => x.MissingDate || x.Expired || x.QuarantinedQuantity > 0)
            .OrderBy(x => x.BatchId).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(items.Select(x => new ExpiryReadinessLot(x.BatchId, x.ItemId, x.ItemName, x.BatchNumber, x.ExpiryDate,
            x.PhysicalQuantity, x.QuarantinedQuantity, x.MissingDate, x.Expired)).ToList(), totals?.Total ?? 0, totals?.Missing ?? 0, totals?.Expired ?? 0, totals?.Quarantined ?? 0, totals?.Physical ?? 0, totals?.Held ?? 0);
    }
}
