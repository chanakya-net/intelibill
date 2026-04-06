namespace Intelibill.Application.Features.Inventory.DTOs;

public sealed record EditInventoryBatchResultDto(
    Guid InventoryBatchId,
    string BatchNumber,
    decimal Quantity,
    decimal CostPrice,
    decimal Mrp,
    decimal SalesPrice,
    decimal TaxRatePercent,
    bool TaxIncluded,
    Guid? SupplierId);
