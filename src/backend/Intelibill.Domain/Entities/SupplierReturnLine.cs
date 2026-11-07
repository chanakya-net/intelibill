using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

public sealed class SupplierReturnLine : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid SupplierReturnId { get; private set; }
    public Guid ReceiptValueId { get; private set; }
    public Guid InventoryBatchId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid? PurchaseOrderReceiptLineId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal ProposedCreditAmount { get; private set; }
    public decimal ReceivedQuantity { get; private set; }
    public decimal EffectiveTotalCost { get; private set; }
    public string ItemName { get; private set; } = string.Empty;
    public string Uqc { get; private set; } = string.Empty;
    public string? BatchNumberSnapshot { get; private set; }
    public string? SourceReference { get; private set; }
    public Guid StockTransactionId { get; private set; }
    private SupplierReturnLine() { }
    public static ErrorOr<SupplierReturnLine> Create(Guid shopId, Guid returnId, Guid receiptId, Guid batchId, Guid itemId, Guid? poLineId, decimal quantity, decimal proposed, decimal received, decimal effective, string item, string uqc, string? reference, Guid movement, string? batchNumberSnapshot = null)
    {
        if (quantity <= 0 || decimal.Round(quantity,3) != quantity || quantity > received || proposed < 0 || proposed > PurchaseReceiptValue.MaxTotalCost || decimal.Round(proposed,2) != proposed)
            return Error.Validation("SupplierReturn.LineInvalid", "Quantity and proposed credit must be within receipt limits and precision.");
        return new SupplierReturnLine { ShopId=shopId, SupplierReturnId=returnId, ReceiptValueId=receiptId, InventoryBatchId=batchId, ItemId=itemId, PurchaseOrderReceiptLineId=poLineId, Quantity=quantity, ProposedCreditAmount=proposed, ReceivedQuantity=received, EffectiveTotalCost=effective, ItemName=item, Uqc=uqc, SourceReference=reference, StockTransactionId=movement, BatchNumberSnapshot=batchNumberSnapshot };
    }
}
