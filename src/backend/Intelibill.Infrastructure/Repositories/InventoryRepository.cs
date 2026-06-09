using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class InventoryRepository(ApplicationDbContext context)
    : RepositoryBase<Inventory>(context), IInventoryRepository
{
    public async Task<Inventory?> GetByItemAsync(Guid shopId, Guid itemId, CancellationToken cancellationToken = default) =>
        await DbSet.FirstOrDefaultAsync(i => i.ShopId == shopId && i.ItemId == itemId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetQuantitiesByShopIdAsync(Guid shopId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(i => i.ShopId == shopId)
            .ToDictionaryAsync(i => i.ItemId, i => i.Quantity, cancellationToken);

    public async Task<IReadOnlyList<Inventory>> GetByItemIdsAsync(Guid shopId, IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(i => i.ShopId == shopId && itemIds.Contains(i.ItemId))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Inventory>> GetAllByShopWithItemAsync(Guid shopId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(i => i.Item)
            .Where(i => i.ShopId == shopId)
            .ToListAsync(cancellationToken);

    public async Task<int> CountLowStockItemsByShopAsync(Guid shopId, CancellationToken cancellationToken = default) =>
        await DbSet.CountAsync(
            i => i.ShopId == shopId
                 && i.ReorderLevel > 0m
                 && i.Quantity <= i.ReorderLevel,
            cancellationToken);
}
