using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

public sealed class InventoryAdjustment : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid InventoryBatchId { get; private set; }
    public string AdjustmentNumber { get; private set; } = string.Empty;
    public InventoryAdjustmentDirection Direction { get; private set; }
    public InventoryAdjustmentReason Reason { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal CostImpact { get; private set; }
    public decimal BatchQuantityBefore { get; private set; }
    public decimal BatchQuantityAfter { get; private set; }
    public decimal InventoryQuantityBefore { get; private set; }
    public decimal InventoryQuantityAfter { get; private set; }
    public DateTimeOffset PerformedAt { get; private set; }
    public Guid PerformedBy { get; private set; }
    public string? Notes { get; private set; }
    public bool IsVoided { get; private set; }
    public string? VoidReason { get; private set; }
    public Guid? VoidedBy { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public Guid? ReversalStockTransactionId { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public Item Item { get; private set; } = null!;
    public InventoryBatch InventoryBatch { get; private set; } = null!;
    public StockTransaction? ReversalStockTransaction { get; private set; }

    private InventoryAdjustment() { }

    public static ErrorOr<InventoryAdjustment> Create(
        Guid shopId,
        Guid itemId,
        Guid inventoryBatchId,
        string adjustmentNumber,
        InventoryAdjustmentDirection direction,
        InventoryAdjustmentReason reason,
        decimal quantity,
        decimal unitCost,
        decimal costImpact,
        decimal batchQuantityBefore,
        decimal batchQuantityAfter,
        decimal inventoryQuantityBefore,
        decimal inventoryQuantityAfter,
        DateTimeOffset performedAt,
        Guid performedBy,
        string? notes,
        Guid createdBy)
    {
        var validation = ValidateAdjustment(
            adjustmentNumber,
            direction,
            reason,
            quantity,
            unitCost,
            costImpact,
            batchQuantityBefore,
            batchQuantityAfter,
            inventoryQuantityBefore,
            inventoryQuantityAfter,
            notes);
        if (validation.IsError)
        {
            return validation.Errors;
        }

        return new InventoryAdjustment
        {
            ShopId = shopId,
            ItemId = itemId,
            InventoryBatchId = inventoryBatchId,
            AdjustmentNumber = adjustmentNumber.Trim(),
            Direction = direction,
            Reason = reason,
            Quantity = quantity,
            UnitCost = unitCost,
            CostImpact = costImpact,
            BatchQuantityBefore = batchQuantityBefore,
            BatchQuantityAfter = batchQuantityAfter,
            InventoryQuantityBefore = inventoryQuantityBefore,
            InventoryQuantityAfter = inventoryQuantityAfter,
            PerformedAt = performedAt,
            PerformedBy = performedBy,
            Notes = NormalizeOptional(notes),
            IsVoided = false,
            CreatedBy = createdBy,
        };
    }

    public ErrorOr<Success> Void(DateTimeOffset voidedAt, Guid voidedBy, string reason, Guid reversalStockTransactionId)
    {
        if (IsVoided)
        {
            return Error.Validation("InventoryAdjustment.AlreadyVoided", "Inventory adjustment is already voided.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("InventoryAdjustment.VoidReasonRequired", "Void reason is required.");
        }

        if (voidedBy == Guid.Empty)
        {
            return Error.Validation("InventoryAdjustment.VoidedByRequired", "Voided-by user is required.");
        }

        if (reversalStockTransactionId == Guid.Empty)
        {
            return Error.Validation("InventoryAdjustment.ReversalStockTransactionRequired", "Reversal stock transaction is required.");
        }

        IsVoided = true;
        VoidedAt = voidedAt;
        VoidedBy = voidedBy;
        VoidReason = reason.Trim();
        ReversalStockTransactionId = reversalStockTransactionId;
        UpdatedBy = voidedBy;

        return Result.Success;
    }

    public void MarkUpdatedBy(Guid updatedBy)
    {
        UpdatedBy = updatedBy;
    }

    private static ErrorOr<Success> ValidateAdjustment(
        string adjustmentNumber,
        InventoryAdjustmentDirection direction,
        InventoryAdjustmentReason reason,
        decimal quantity,
        decimal unitCost,
        decimal costImpact,
        decimal batchQuantityBefore,
        decimal batchQuantityAfter,
        decimal inventoryQuantityBefore,
        decimal inventoryQuantityAfter,
        string? notes)
    {
        if (string.IsNullOrWhiteSpace(adjustmentNumber))
        {
            return Error.Validation("InventoryAdjustment.AdjustmentNumberRequired", "Adjustment number is required.");
        }

        if (quantity <= 0)
        {
            return Error.Validation("InventoryAdjustment.QuantityMustBePositive", "Adjustment quantity must be greater than zero.");
        }

        if (HasMoreThanTwoDecimalPlaces(quantity))
        {
            return Error.Validation("InventoryAdjustment.QuantityScaleInvalid", "Adjustment quantity cannot have more than two decimal places.");
        }

        if (unitCost < 0)
        {
            return Error.Validation("InventoryAdjustment.UnitCostNegative", "Unit cost cannot be negative.");
        }

        if (costImpact <= 0)
        {
            return Error.Validation("InventoryAdjustment.CostImpactMustBePositive", "Cost impact must be greater than zero.");
        }

        if (batchQuantityBefore < 0 || batchQuantityAfter < 0 || inventoryQuantityBefore < 0 || inventoryQuantityAfter < 0)
        {
            return Error.Validation("InventoryAdjustment.QuantitySnapshotNegative", "Quantity snapshots cannot be negative.");
        }

        if (!IsReasonAllowedForDirection(direction, reason))
        {
            return Error.Validation("InventoryAdjustment.ReasonDirectionMismatch", "Adjustment reason is not valid for the direction.");
        }

        if ((reason is InventoryAdjustmentReason.OtherLoss or InventoryAdjustmentReason.OtherGain) && string.IsNullOrWhiteSpace(notes))
        {
            return Error.Validation("InventoryAdjustment.NotesRequired", "Notes are required for other loss or other gain adjustments.");
        }

        if (direction == InventoryAdjustmentDirection.Decrease
            && (batchQuantityBefore - quantity != batchQuantityAfter || inventoryQuantityBefore - quantity != inventoryQuantityAfter))
        {
            return Error.Validation("InventoryAdjustment.QuantitySnapshotMismatch", "Decrease adjustment snapshots must subtract the adjustment quantity.");
        }

        if (direction == InventoryAdjustmentDirection.Increase
            && (batchQuantityBefore + quantity != batchQuantityAfter || inventoryQuantityBefore + quantity != inventoryQuantityAfter))
        {
            return Error.Validation("InventoryAdjustment.QuantitySnapshotMismatch", "Increase adjustment snapshots must add the adjustment quantity.");
        }

        return Result.Success;
    }

    private static bool IsReasonAllowedForDirection(InventoryAdjustmentDirection direction, InventoryAdjustmentReason reason)
    {
        return direction switch
        {
            InventoryAdjustmentDirection.Decrease => reason is InventoryAdjustmentReason.Damaged
                or InventoryAdjustmentReason.Expired
                or InventoryAdjustmentReason.Stolen
                or InventoryAdjustmentReason.MissingLost
                or InventoryAdjustmentReason.StockCountCorrection
                or InventoryAdjustmentReason.OtherLoss,
            InventoryAdjustmentDirection.Increase => reason is InventoryAdjustmentReason.FoundStock
                or InventoryAdjustmentReason.StockCountCorrection
                or InventoryAdjustmentReason.ReturnRestockCorrection
                or InventoryAdjustmentReason.OtherGain,
            _ => false,
        };
    }

    private static bool HasMoreThanTwoDecimalPlaces(decimal value) =>
        decimal.Truncate(value * 100m) != value * 100m;

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
