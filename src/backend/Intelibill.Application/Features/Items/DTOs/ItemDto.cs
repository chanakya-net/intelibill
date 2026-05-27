namespace Intelibill.Application.Features.Items.DTOs;

public sealed record ItemDto(
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

public sealed record ItemCatalogResultDto(
    IReadOnlyList<ItemDto> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    ItemCatalogSummaryDto Summary);

public sealed record ItemCatalogSummaryDto(
    int TotalItems,
    int ActiveItems,
    int InactiveItems,
    int RunningLowStockCount,
    int CriticalStockCount,
    decimal TotalStockValue);
