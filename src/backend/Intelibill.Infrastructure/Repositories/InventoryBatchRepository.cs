using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class InventoryBatchRepository(ApplicationDbContext context)
    : RepositoryBase<InventoryBatch>(context), IInventoryBatchRepository
{
    public async Task<IReadOnlyList<InventoryBatch>> GetByItemAsync(Guid shopId, Guid itemId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(b => b.ShopId == shopId && b.ItemId == itemId)
            .OrderBy(b => b.ExpiryDate)
            .ThenBy(b => b.BatchNumber)
            .ToListAsync(cancellationToken);

    public async Task<InventoryBatch?> GetByBatchNumberAsync(Guid shopId, Guid itemId, string batchNumber, CancellationToken cancellationToken = default)
    {
        var normalizedBatchNumber = batchNumber.Trim();
        return await DbSet.FirstOrDefaultAsync(
            b => b.ShopId == shopId && b.ItemId == itemId && b.BatchNumber == normalizedBatchNumber,
            cancellationToken);
    }
}