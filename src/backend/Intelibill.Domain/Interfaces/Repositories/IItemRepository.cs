using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IItemRepository : IRepository<Item>
{
    Task<Item?> GetByBarcodeAsync(Guid shopId, string barcode, CancellationToken cancellationToken = default);
    Task<Item?> GetByNameAsync(Guid shopId, string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Item>> GetByShopIdAsync(Guid shopId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<Item> StreamByShopIdAsync(Guid shopId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Item>> GetByBarcodesAsync(Guid shopId, IReadOnlyList<string> barcodes, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Item>> GetByIdsAsync(Guid shopId, IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken = default);
    Task<ItemCatalogResultReadModel> GetCatalogAsync(ItemCatalogFilter filter, CancellationToken cancellationToken = default);
}

public sealed record ItemCatalogFilter(
    Guid ShopId,
    string? Search,
    string? Status,
    int PageNumber,
    int PageSize);

public sealed record ItemCatalogResultReadModel(
    IReadOnlyList<ItemCatalogReadModel> Items,
    int TotalCount,
    ItemCatalogSummaryReadModel Summary);

public sealed record ItemCatalogReadModel(
    Guid Id,
    string Name,
    string Barcode,
    string? Description,
    string Uom,
    bool IsActive,
    decimal CurrentStock,
    decimal UnitPrice,
    decimal CurrentStockValue,
    decimal ReorderLevel,
    string StockStatus,
    string? HsnCode,
    decimal DefaultTaxRatePercent,
    bool DefaultTaxIncluded);

public sealed record ItemCatalogSummaryReadModel(
    int TotalItems,
    int ActiveItems,
    int InactiveItems,
    int RunningLowStockCount,
    int CriticalStockCount,
    decimal TotalStockValue);
