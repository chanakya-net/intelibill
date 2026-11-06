using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IItemRepository : IRepository<Item>
{
    Task<Item?> GetByBarcodeAsync(Guid shopId, string barcode, CancellationToken cancellationToken = default);
    Task<Item?> GetByNameAsync(Guid shopId, string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Item>> GetByShopIdAsync(Guid shopId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<Item> StreamByShopIdAsync(Guid shopId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Item>> GetByBarcodesAsync(Guid shopId, IReadOnlyList<string> barcodes, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Item>> GetByIdsAsync(Guid shopId, IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the item has any stock history (any batch, voided or not, or any stock transaction) and the
    /// finest decimal scale held by its non-voided, positive on-hand batches.
    /// </summary>
    Task<ItemStockHistory> GetStockHistoryAsync(Guid shopId, Guid itemId, CancellationToken cancellationToken = default);
}
