using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class StockTransactionRepository(ApplicationDbContext context)
    : RepositoryBase<StockTransaction>(context), IStockTransactionRepository
{
    public async Task<IReadOnlyList<StockTransaction>> GetByItemAsync(Guid shopId, Guid itemId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(t => t.ShopId == shopId && t.ItemId == itemId)
            .OrderByDescending(t => t.PerformedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StockTransaction>> GetByBatchAsync(Guid shopId, Guid inventoryBatchId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(t => t.ShopId == shopId && t.InventoryBatchId == inventoryBatchId)
            .OrderByDescending(t => t.PerformedAt)
            .ToListAsync(cancellationToken);
}
