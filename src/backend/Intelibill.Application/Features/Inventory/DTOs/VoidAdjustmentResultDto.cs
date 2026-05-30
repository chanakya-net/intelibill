namespace Intelibill.Application.Features.Inventory.DTOs;

public sealed record VoidAdjustmentResultDto(
    Guid AdjustmentId,
    Guid ReversalStockTransactionId,
    decimal BatchQuantityBefore,
    decimal BatchQuantityAfter,
    decimal InventoryQuantityBefore,
    decimal InventoryQuantityAfter,
    DateTimeOffset VoidedAt);
