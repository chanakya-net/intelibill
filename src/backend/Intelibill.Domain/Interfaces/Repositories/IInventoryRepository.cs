using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IInventoryRepository : IRepository<Inventory>
{
    Task<Inventory?> GetByItemAsync(Guid shopId, Guid itemId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, decimal>> GetQuantitiesByShopIdAsync(Guid shopId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Inventory>> GetByItemIdsAsync(Guid shopId, IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Inventory>> GetAllByShopWithItemAsync(Guid shopId, CancellationToken cancellationToken = default);
    Task<int> CountLowStockItemsByShopAsync(Guid shopId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LowStockAlertReadModel>> GetTopLowStockAlertsAsync(Guid shopId, CancellationToken cancellationToken = default);
}

public sealed record LowStockAlertReadModel(Guid ItemId, string ItemName, decimal Quantity, decimal ReorderLevel);
