namespace Intelibill.Application.Features.Inventory.DTOs;

public sealed record InventoryAdjustmentResultDto(
    Guid AdjustmentId,
    string AdjustmentNumber,
    Guid BatchId,
    decimal Quantity,
    decimal UnitCost,
    decimal CostImpact,
    decimal BatchQuantityBefore,
    decimal BatchQuantityAfter,
    decimal InventoryQuantityBefore,
    decimal InventoryQuantityAfter,
    Guid StockTransactionId,
    DateTimeOffset PerformedAt);
