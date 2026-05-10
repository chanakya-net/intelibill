using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class DiscountRuleRepository(ApplicationDbContext context)
    : RepositoryBase<DiscountRule>(context), IDiscountRuleRepository
{
    public async Task<IReadOnlyList<DiscountRule>> GetByShopAsync(
        Guid shopId,
        CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(r => r.ShopId == shopId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DiscountRule>> GetActiveByShopAsync(
        Guid shopId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(r => r.ShopId == shopId
                && r.IsActive
                && (r.StartsAt == null || r.StartsAt <= now)
                && (r.EndsAt == null || r.EndsAt > now))
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DiscountRule>> GetUpcomingByShopAsync(
        Guid shopId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(r => r.ShopId == shopId
                && r.IsActive
                && r.StartsAt != null
                && r.StartsAt > now)
            .OrderBy(r => r.StartsAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DiscountRule>> GetActiveByBatchAsync(
        Guid shopId,
        Guid batchId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(r => r.ShopId == shopId
                && r.InventoryBatchId == batchId
                && r.IsActive
                && (r.StartsAt == null || r.StartsAt <= now)
                && (r.EndsAt == null || r.EndsAt > now))
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DiscountRule>> GetDisabledByShopAsync(
        Guid shopId,
        CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(r => r.ShopId == shopId && !r.IsActive)
            .OrderByDescending(r => r.DisabledAt)
            .ToListAsync(cancellationToken);
}
