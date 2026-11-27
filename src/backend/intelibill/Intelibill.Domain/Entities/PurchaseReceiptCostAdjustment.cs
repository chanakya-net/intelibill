using ErrorOr;
using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

/// <summary>
/// One correction of a receipt's total cost: the total before and after, the signed difference
/// posted to the supplier payable, who made it and why.
/// </summary>
/// <remarks>
/// An immutable posted fact. Sale cost snapshots and the batch's unit cost are not touched: the
/// delta is a commercial correction of what is owed, not a restatement of goods already sold.
/// </remarks>
public sealed class PurchaseReceiptCostAdjustment : BaseEntity
{
    public const int MaxReasonLength = 500;

    public Guid ShopId { get; private set; }
    public Guid PurchaseReceiptValueId { get; private set; }
    public Guid InventoryBatchId { get; private set; }
    public decimal PriorTotalCost { get; private set; }
    public decimal RevisedTotalCost { get; private set; }
    public decimal Delta { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid SupplierLedgerEntryId { get; private set; }
    public DateTimeOffset PerformedAt { get; private set; }
    public Guid PerformedBy { get; private set; }

    private PurchaseReceiptCostAdjustment() { }

    public static ErrorOr<PurchaseReceiptCostAdjustment> Create(
        Guid shopId,
        Guid purchaseReceiptValueId,
        Guid inventoryBatchId,
        decimal priorTotalCost,
        decimal revisedTotalCost,
        string reason,
        Guid supplierLedgerEntryId,
        DateTimeOffset performedAt,
        Guid performedBy)
    {
        if (priorTotalCost < 0 || revisedTotalCost < 0)
            return Error.Validation("PurchaseReceipt.TotalCostInvalid", "Receipt totals cannot be negative.");

        if (priorTotalCost == revisedTotalCost)
            return Error.Validation("PurchaseReceipt.CostUnchanged", "The revised total equals the current receipt total.");

        if (string.IsNullOrWhiteSpace(reason))
            return Error.Validation("PurchaseReceipt.ReasonRequired", "A reason is required for a receipt cost correction.");

        if (reason.Trim().Length > MaxReasonLength)
            return Error.Validation("PurchaseReceipt.ReasonTooLong", $"Reason cannot exceed {MaxReasonLength} characters.");

        return new PurchaseReceiptCostAdjustment
        {
            ShopId = shopId,
            PurchaseReceiptValueId = purchaseReceiptValueId,
            InventoryBatchId = inventoryBatchId,
            PriorTotalCost = priorTotalCost,
            RevisedTotalCost = revisedTotalCost,
            Delta = revisedTotalCost - priorTotalCost,
            Reason = reason.Trim(),
            SupplierLedgerEntryId = supplierLedgerEntryId,
            PerformedAt = performedAt,
            PerformedBy = performedBy,
        };
    }
}
