using ErrorOr;
using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

public sealed class SaleCancellationLine : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid SaleCancellationId { get; private set; }
    public Guid SaleId { get; private set; }
    public Guid SaleItemId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid InventoryBatchId { get; private set; }
    public Guid StockTransactionId { get; private set; }
    public decimal Quantity { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string Uqc { get; private set; } = string.Empty;
    public decimal OriginalCostPrice { get; private set; }
    public decimal RevenueAmount { get; private set; }
    public decimal RecordedCostAmount { get; private set; }

    private SaleCancellationLine() { }

    public static ErrorOr<SaleCancellationLine> Create(Guid shopId, Guid saleCancellationId, Guid saleId, Guid saleItemId,
        Guid itemId, Guid inventoryBatchId, Guid stockTransactionId, decimal quantity, string description, string uqc,
        decimal originalCostPrice, decimal revenueAmount, decimal recordedCostAmount)
    {
        if (shopId == Guid.Empty || saleCancellationId == Guid.Empty || saleId == Guid.Empty || saleItemId == Guid.Empty ||
            itemId == Guid.Empty || inventoryBatchId == Guid.Empty || stockTransactionId == Guid.Empty ||
            quantity <= 0 || originalCostPrice < 0 || revenueAmount < 0 || recordedCostAmount < 0 ||
            string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(uqc))
            return Error.Validation("SaleCancellation.LineInvalid", "Cancellation lines require original stock links, positive quantity and nonnegative amounts.");

        return new SaleCancellationLine
        {
            ShopId = shopId, SaleCancellationId = saleCancellationId, SaleId = saleId, SaleItemId = saleItemId,
            ItemId = itemId, InventoryBatchId = inventoryBatchId, StockTransactionId = stockTransactionId,
            Quantity = quantity, Description = description, Uqc = uqc, OriginalCostPrice = originalCostPrice,
            RevenueAmount = revenueAmount, RecordedCostAmount = recordedCostAmount,
        };
    }
}
