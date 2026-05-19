namespace Intelibill.Application.Features.Inventory.DTOs;

public sealed record InventoryBatchDto(
    Guid Id,
    Guid ShopId,
    Guid ItemId,
    string ItemName,
    string Barcode,
    string BatchNumber,
    decimal Quantity,
    decimal OriginalQuantity,
    decimal CostPrice,
    decimal Mrp,
    decimal SalesPrice,
    decimal TaxRatePercent,
    bool TaxIncluded,
    bool PurchaseTaxIncluded,
    DateOnly? ExpiryDate,
    DateOnly? ManufacturingDate,
    Guid? SupplierId,
    string? SupplierName,
    bool IsVoided,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
