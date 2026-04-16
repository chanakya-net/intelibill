namespace Intelibill.Application.Features.Sales.DTOs;

public sealed record SaleItemDto(
    Guid SaleItemId,
    Guid ItemId,
    Guid InventoryBatchId,
    decimal Quantity,
    decimal SalesPrice,
    decimal TaxRatePercent,
    bool HasPriceMismatch);
