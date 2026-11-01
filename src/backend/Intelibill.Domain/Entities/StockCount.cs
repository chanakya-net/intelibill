using ErrorOr;
using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

/// <summary>
/// One physical count of a batch: what the system held, what was counted, who counted it and why.
/// </summary>
/// <remarks>
/// A count is an immutable posted fact. A nonzero delta is applied through exactly one
/// <see cref="InventoryAdjustment"/> and one signed <see cref="StockTransaction"/>, both linked here;
/// an equal count is a confirmation and links neither, because nothing physical moved.
/// </remarks>
public sealed class StockCount : BaseEntity
{
    public const int MaxReasonLength = 500;

    public Guid ShopId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid InventoryBatchId { get; private set; }
    public uint ExpectedBatchVersion { get; private set; }
    public decimal ObservedQuantity { get; private set; }
    public decimal CountedQuantity { get; private set; }
    public decimal Delta { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset PerformedAt { get; private set; }
    public Guid PerformedBy { get; private set; }
    public Guid? InventoryAdjustmentId { get; private set; }
    public Guid? StockTransactionId { get; private set; }

    private StockCount() { }

    public static ErrorOr<StockCount> Create(
        Guid shopId,
        Guid itemId,
        Guid inventoryBatchId,
        uint expectedBatchVersion,
        decimal observedQuantity,
        decimal countedQuantity,
        string reason,
        DateTimeOffset performedAt,
        Guid performedBy,
        Guid? inventoryAdjustmentId,
        Guid? stockTransactionId)
    {
        if (observedQuantity < 0 || countedQuantity < 0)
            return Error.Validation("StockCount.QuantityNegative", "Counted and observed quantities cannot be negative.");

        if (string.IsNullOrWhiteSpace(reason))
            return Error.Validation("StockCount.ReasonRequired", "A reason is required for a stock count.");

        if (reason.Trim().Length > MaxReasonLength)
            return Error.Validation("StockCount.ReasonTooLong", $"Reason cannot exceed {MaxReasonLength} characters.");

        var delta = countedQuantity - observedQuantity;

        // The links are the audit trail: a change without its movement, or a movement without a
        // change, is exactly the discrepancy this record exists to rule out.
        if ((delta == 0) != (inventoryAdjustmentId is null) || (delta == 0) != (stockTransactionId is null))
            return Error.Validation("StockCount.MovementLinkMismatch", "A count must link one movement exactly when its quantity changed.");

        return new StockCount
        {
            ShopId = shopId,
            ItemId = itemId,
            InventoryBatchId = inventoryBatchId,
            ExpectedBatchVersion = expectedBatchVersion,
            ObservedQuantity = observedQuantity,
            CountedQuantity = countedQuantity,
            Delta = delta,
            Reason = reason.Trim(),
            PerformedAt = performedAt,
            PerformedBy = performedBy,
            InventoryAdjustmentId = inventoryAdjustmentId,
            StockTransactionId = stockTransactionId,
        };
    }
}
