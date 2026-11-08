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

    public Guid? QuarantineChangeId { get; private set; }
    public decimal QuarantinedQuantity { get; private set; }
    public string? DispositionReasonCode { get; private set; }
    public void FreezeDisposition(Guid? root, decimal quantity, string? reason)
    {
        if (QuarantineChangeId.HasValue || QuarantinedQuantity != 0 || quantity < 0 || quantity > Quantity || (quantity > 0) != root.HasValue)
            throw new InvalidOperationException("Invalid original disposition.");
        QuarantineChangeId = root; QuarantinedQuantity = quantity; DispositionReasonCode = reason;
    }
    private SaleCancellationLine() { }

    internal static ErrorOr<SaleCancellationLine> Create(Guid cancellationId, SaleCancellationLineInput input)
    {
        if (input.ShopId == Guid.Empty || cancellationId == Guid.Empty || input.SaleId == Guid.Empty || input.SaleItemId == Guid.Empty ||
            input.ItemId == Guid.Empty || input.InventoryBatchId == Guid.Empty || input.StockTransactionId == Guid.Empty ||
            input.Quantity <= 0 || input.OriginalCostPrice < 0 || input.RevenueAmount < 0 || input.RecordedCostAmount < 0 ||
            string.IsNullOrWhiteSpace(input.Description) || string.IsNullOrWhiteSpace(input.Uqc))
            return Error.Validation("SaleCancellation.LineInvalid", "Cancellation lines require original stock links, positive quantity and nonnegative amounts.");

        return new SaleCancellationLine
        {
            ShopId = input.ShopId, SaleCancellationId = cancellationId, SaleId = input.SaleId, SaleItemId = input.SaleItemId,
            ItemId = input.ItemId, InventoryBatchId = input.InventoryBatchId, StockTransactionId = input.StockTransactionId,
            Quantity = input.Quantity, Description = input.Description, Uqc = input.Uqc, OriginalCostPrice = input.OriginalCostPrice,
            RevenueAmount = input.RevenueAmount, RecordedCostAmount = input.RecordedCostAmount,
        };
    }
}
