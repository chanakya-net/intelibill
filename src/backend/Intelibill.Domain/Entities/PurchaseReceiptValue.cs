using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

/// <summary>
/// The commercial fact of one receipt of goods: how much was received from whom, and what the
/// supplier is owed for it. One per <see cref="InventoryBatch"/>, which stays the physical lot.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OriginalTotalCost"/> is the total the payable was posted with and never changes; it is
/// kept as a total so the rounding remainder of a per-unit cost is not lost.
/// <see cref="EffectiveTotalCost"/> is the original plus every
/// <see cref="PurchaseReceiptCostAdjustment"/> and moves only through <see cref="ApplyCostCorrection"/>.
/// </para>
/// <para>
/// A <see cref="PurchaseReceiptValueStatus.LegacyUnresolved"/> receipt has no known amounts: they are
/// left null rather than estimated from remaining stock, and it cannot be corrected.
/// </para>
/// </remarks>
public sealed class PurchaseReceiptValue : BaseEntity
{
    public const int MaxSourceReferenceLength = 80;

    /// <summary>The largest total a supplier ledger entry can carry (numeric(10,2)).</summary>
    public const decimal MaxTotalCost = 99_999_999.99m;

    public Guid ShopId { get; private set; }
    public Guid InventoryBatchId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid? SupplierId { get; private set; }
    public decimal ReceivedQuantity { get; private set; }
    public decimal? OriginalTotalCost { get; private set; }
    public decimal? EffectiveTotalCost { get; private set; }
    public PurchaseReceiptSource Source { get; private set; }
    public PurchaseReceiptValueStatus Status { get; private set; }
    public string? SourceReference { get; private set; }
    public Guid? PurchaseOrderReceiptLineId { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public Guid CreatedBy { get; private set; }

    /// <summary>Row version; a correction is accepted only against the version it was entered at.</summary>
    public uint Version { get; private set; }

    public bool IsResolved => Status == PurchaseReceiptValueStatus.Resolved;

    private PurchaseReceiptValue() { }

    public static ErrorOr<PurchaseReceiptValue> Create(
        Guid shopId,
        Guid inventoryBatchId,
        Guid itemId,
        Guid? supplierId,
        decimal receivedQuantity,
        decimal totalCost,
        string? sourceReference,
        DateTimeOffset receivedAt,
        Guid createdBy)
    {
        if (receivedQuantity <= 0)
            return Error.Validation("PurchaseReceipt.QuantityInvalid", "Received quantity must be greater than zero.");

        if (totalCost < 0 || totalCost > MaxTotalCost || decimal.Round(totalCost, 2) != totalCost)
            return Error.Validation("PurchaseReceipt.TotalCostInvalid", "Receipt total must be between 0 and 99,999,999.99 with at most two decimals.");

        return new PurchaseReceiptValue
        {
            ShopId = shopId,
            InventoryBatchId = inventoryBatchId,
            ItemId = itemId,
            SupplierId = supplierId,
            ReceivedQuantity = receivedQuantity,
            OriginalTotalCost = totalCost,
            EffectiveTotalCost = totalCost,
            Source = PurchaseReceiptSource.DirectInbound,
            Status = PurchaseReceiptValueStatus.Resolved,
            SourceReference = NormalizeReference(sourceReference),
            ReceivedAt = receivedAt.ToUniversalTime(),
            CreatedBy = createdBy,
        };
    }

    /// <summary>Marks the receipt as received against a purchase order line.</summary>
    public void LinkPurchaseOrderReceiptLine(Guid purchaseOrderReceiptLineId)
    {
        Source = PurchaseReceiptSource.PurchaseOrder;
        PurchaseOrderReceiptLineId = purchaseOrderReceiptLineId;
    }

    /// <summary>
    /// Moves the effective total to <paramref name="revisedTotalCost"/> and returns the signed
    /// difference that must be posted to the supplier payable.
    /// </summary>
    public ErrorOr<decimal> ApplyCostCorrection(decimal revisedTotalCost)
    {
        if (!IsResolved || EffectiveTotalCost is not { } current)
            return Error.Validation("PurchaseReceipt.EvidenceMissing", "The original receipt total is not on record, so it cannot be corrected.");

        if (revisedTotalCost < 0 || revisedTotalCost > MaxTotalCost || decimal.Round(revisedTotalCost, 2) != revisedTotalCost)
            return Error.Validation("PurchaseReceipt.TotalCostInvalid", "Receipt total must be between 0 and 99,999,999.99 with at most two decimals.");

        var delta = revisedTotalCost - current;
        if (delta == 0)
            return Error.Validation("PurchaseReceipt.CostUnchanged", "The revised total equals the current receipt total.");

        EffectiveTotalCost = revisedTotalCost;
        return delta;
    }

    public void ReassignSupplier(Guid supplierId)
    {
        SupplierId = supplierId;
    }

    private static string? NormalizeReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= MaxSourceReferenceLength ? trimmed : trimmed[..MaxSourceReferenceLength];
    }
}
