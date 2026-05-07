using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

public sealed class SaleItem : BaseEntity
{
    public Guid SaleId { get; private set; }
    public Guid ShopId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid InventoryBatchId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal CostPrice { get; private set; }
    public decimal SalesPrice { get; private set; }
    public decimal Mrp { get; private set; }
    public decimal TaxRatePercent { get; private set; }
    public bool IsPriceIncludingTax { get; private set; }
    public bool HasPriceMismatch { get; private set; }

    private SaleItem() { }

    internal static SaleItem Create(
        Guid shopId,
        Guid itemId,
        Guid inventoryBatchId,
        decimal quantity,
        decimal costPrice,
        decimal salesPrice,
        decimal mrp,
        decimal taxRatePercent,
        bool isPriceIncludingTax,
        bool hasPriceMismatch)
    {
        return new SaleItem
        {
            ShopId = shopId,
            ItemId = itemId,
            InventoryBatchId = inventoryBatchId,
            Quantity = quantity,
            CostPrice = costPrice,
            SalesPrice = salesPrice,
            Mrp = mrp,
            TaxRatePercent = taxRatePercent,
            IsPriceIncludingTax = isPriceIncludingTax,
            HasPriceMismatch = hasPriceMismatch,
        };
    }
}
