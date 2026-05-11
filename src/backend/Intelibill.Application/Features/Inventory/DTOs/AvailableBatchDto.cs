namespace Intelibill.Application.Features.Inventory.DTOs;

public sealed record AvailableBatchDto(
    string Barcode,
    string ItemName,
    string BatchNumber,
    Guid InventoryBatchId,
    decimal Quantity,
    decimal SalesPrice,
    decimal Mrp,
    decimal TaxRatePercent,
    bool TaxIncluded,
    DateOnly? ExpiryDate);
