namespace Intelibill.Application.Features.Inventory.DTOs;

public sealed record AddInventoryResultDto(
    Guid ItemId,
    string ItemName,
    string Barcode,
    Guid InventoryBatchId,
    string BatchNumber,
    decimal BatchQuantity,
    decimal TotalQuantity,
    Guid StockTransactionId,
    DateTimeOffset PerformedAt);