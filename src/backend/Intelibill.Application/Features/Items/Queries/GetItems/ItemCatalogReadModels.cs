namespace Intelibill.Application.Features.Items.Queries.GetItems;

public interface IItemCatalogRepository
{
    Task<ItemCatalogResultReadModel> GetCatalogAsync(
        ItemCatalogFilter filter,
        CancellationToken cancellationToken = default);
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
