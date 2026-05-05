using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IInventoryAdjustmentRepository : IRepository<InventoryAdjustment>
{
    Task<InventoryAdjustment?> GetByAdjustmentNumberAsync(Guid shopId, string adjustmentNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InventoryAdjustment>> GetByBatchAsync(Guid shopId, Guid inventoryBatchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InventoryAdjustment>> GetByShopAndDateRangeAsync(Guid shopId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);
}
