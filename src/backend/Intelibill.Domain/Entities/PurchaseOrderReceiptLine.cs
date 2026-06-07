using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

public sealed class PurchaseOrderReceiptLine : BaseEntity
{
    public Guid PurchaseOrderReceiptId { get; private set; }
    public Guid PurchaseOrderLineId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid InventoryBatchId { get; private set; }
    public Guid StockTransactionId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal TotalPurchaseCost { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal Mrp { get; private set; }
    public decimal SalesPrice { get; private set; }
    public decimal TaxRatePercent { get; private set; }
    public bool TaxIncluded { get; private set; }
    public bool PurchaseTaxIncluded { get; private set; }
    public InventoryBatch? InventoryBatch { get; private set; }

    private PurchaseOrderReceiptLine() { }

    internal static PurchaseOrderReceiptLine Create(
        Guid purchaseOrderReceiptId,
        Guid purchaseOrderLineId,
        Guid itemId,
        Guid inventoryBatchId,
        Guid stockTransactionId,
        decimal quantity,
        decimal totalPurchaseCost,
        decimal mrp,
        decimal salesPrice,
        decimal taxRatePercent,
        bool taxIncluded,
        bool purchaseTaxIncluded)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Receipt quantity must be positive.");

        if (totalPurchaseCost < 0)
            throw new ArgumentOutOfRangeException(nameof(totalPurchaseCost), "Total purchase cost cannot be negative.");

        if (taxRatePercent < 0 || taxRatePercent > 100)
            throw new ArgumentOutOfRangeException(nameof(taxRatePercent), "Tax rate must be between 0 and 100.");

        if (salesPrice > mrp)
            throw new ArgumentOutOfRangeException(nameof(salesPrice), "Sales price cannot exceed MRP.");

        return new PurchaseOrderReceiptLine
        {
            PurchaseOrderReceiptId = purchaseOrderReceiptId,
            PurchaseOrderLineId = purchaseOrderLineId,
            ItemId = itemId,
            InventoryBatchId = inventoryBatchId,
            StockTransactionId = stockTransactionId,
            Quantity = quantity,
            TotalPurchaseCost = totalPurchaseCost,
            UnitCost = decimal.Round(totalPurchaseCost / quantity, 2, MidpointRounding.AwayFromZero),
            Mrp = mrp,
            SalesPrice = salesPrice,
            TaxRatePercent = taxRatePercent,
            TaxIncluded = taxIncluded,
            PurchaseTaxIncluded = purchaseTaxIncluded,
        };
    }
}
