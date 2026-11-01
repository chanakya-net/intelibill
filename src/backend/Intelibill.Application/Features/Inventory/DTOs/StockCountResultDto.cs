namespace Intelibill.Application.Features.Inventory.DTOs;

/// <summary>
/// The outcome of one stock count. <c>AdjustmentId</c>, <c>AdjustmentNumber</c> and
/// <c>StockTransactionId</c> are null exactly when the count matched the system quantity.
/// </summary>
public sealed record StockCountResultDto(
    Guid StockCountId,
    Guid BatchId,
    decimal ObservedQuantity,
    decimal CountedQuantity,
    decimal Delta,
    Guid? AdjustmentId,
    string? AdjustmentNumber,
    Guid? StockTransactionId,
    decimal BatchQuantityAfter,
    decimal InventoryQuantityAfter,
    uint BatchVersion,
    DateTimeOffset PerformedAt);
