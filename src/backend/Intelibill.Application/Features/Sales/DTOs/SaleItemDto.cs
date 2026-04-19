namespace Intelibill.Application.Features.Sales.DTOs;

public sealed record SaleItemDto(
    Guid SaleItemId,
    Guid ItemId,
    string ItemName,
    Guid InventoryBatchId,
    decimal Quantity,
    decimal SalesPrice,
    decimal TaxRatePercent,
    bool IsPriceIncludingTax,
    bool HasPriceMismatch);
