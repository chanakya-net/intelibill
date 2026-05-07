namespace Intelibill.Domain.ValueObjects;

public sealed record SaleLineInput(
    Guid ShopId,
    Guid ItemId,
    Guid InventoryBatchId,
    decimal Quantity,
    decimal CostPrice,
    decimal SalesPrice,
    decimal Mrp,
    decimal TaxRatePercent,
    bool IsPriceIncludingTax,
    bool HasPriceMismatch);
