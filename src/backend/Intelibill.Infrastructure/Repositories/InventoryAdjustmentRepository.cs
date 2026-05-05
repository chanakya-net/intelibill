using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class InventoryAdjustmentRepository(ApplicationDbContext context)
    : RepositoryBase<InventoryAdjustment>(context), IInventoryAdjustmentRepository
{
    public async Task<InventoryAdjustment?> GetByAdjustmentNumberAsync(
        Guid shopId,
        string adjustmentNumber,
        CancellationToken cancellationToken = default)
    {
        var normalizedAdjustmentNumber = adjustmentNumber.Trim();
        return await DbSet
            .Include(a => a.Item)
            .Include(a => a.InventoryBatch)
            .FirstOrDefaultAsync(a => a.ShopId == shopId && a.AdjustmentNumber == normalizedAdjustmentNumber, cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryAdjustment>> GetByBatchAsync(
        Guid shopId,
        Guid inventoryBatchId,
        CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(a => a.Item)
            .Where(a => a.ShopId == shopId && a.InventoryBatchId == inventoryBatchId)
            .OrderByDescending(a => a.PerformedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<InventoryAdjustment>> GetByShopAndDateRangeAsync(
        Guid shopId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var start = startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var exclusiveEnd = endDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        return await DbSet
            .Include(a => a.Item)
            .Include(a => a.InventoryBatch)
            .Where(a => a.ShopId == shopId
                && a.PerformedAt >= start
                && a.PerformedAt < exclusiveEnd)
            .OrderByDescending(a => a.PerformedAt)
            .ToListAsync(cancellationToken);
    }
}
