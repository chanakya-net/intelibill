namespace Intelibill.Application.Features.Inventory.Commands.EditInventoryBatch;

public sealed record EditInventoryBatchCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    Guid InventoryBatchId,
    string BatchNumber,
    decimal Quantity,
    decimal CostPrice,
    decimal Mrp,
    decimal SalesPrice,
    decimal TaxRatePercent,
    bool TaxIncluded,
    DateOnly? ExpiryDate,
    DateOnly? ManufacturingDate,
    Guid? SupplierId,
    string? Notes,
    DateOnly? EntryDate);
