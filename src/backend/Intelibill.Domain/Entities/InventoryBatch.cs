using ErrorOr;
using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

public sealed class InventoryBatch : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid ItemId { get; private set; }
    public string BatchNumber { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal CostPrice { get; private set; }
    public decimal Mrp { get; private set; }
    public decimal SalesPrice { get; private set; }
    public decimal MinSalePrice { get; private set; }
    public decimal TaxRatePercent { get; private set; }
    public DateOnly? ExpiryDate { get; private set; }
    public DateOnly? ManufacturingDate { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public Item Item { get; private set; } = null!;
    public ICollection<StockTransaction> StockTransactions { get; private set; } = [];

    private InventoryBatch() { }

    public static ErrorOr<InventoryBatch> Create(
        Guid shopId,
        Guid itemId,
        string batchNumber,
        decimal quantity,
        decimal costPrice,
        decimal mrp,
        decimal salesPrice,
        decimal minSalePrice,
        decimal taxRatePercent,
        DateOnly? expiryDate,
        DateOnly? manufacturingDate,
        Guid createdBy)
    {
        var validation = ValidateBatch(batchNumber, quantity, salesPrice, minSalePrice, mrp, taxRatePercent);
        if (validation.IsError)
        {
            return validation.Errors;
        }

        return new InventoryBatch
        {
            ShopId = shopId,
            ItemId = itemId,
            BatchNumber = batchNumber.Trim(),
            Quantity = quantity,
            CostPrice = costPrice,
            Mrp = mrp,
            SalesPrice = salesPrice,
            MinSalePrice = minSalePrice,
            TaxRatePercent = taxRatePercent,
            ExpiryDate = expiryDate,
            ManufacturingDate = manufacturingDate,
            CreatedBy = createdBy,
        };
    }

    public ErrorOr<Success> Update(
        string batchNumber,
        decimal quantity,
        decimal costPrice,
        decimal mrp,
        decimal salesPrice,
        decimal minSalePrice,
        decimal taxRatePercent,
        DateOnly? expiryDate,
        DateOnly? manufacturingDate,
        Guid updatedBy)
    {
        var validation = ValidateBatch(batchNumber, quantity, salesPrice, minSalePrice, mrp, taxRatePercent);
        if (validation.IsError)
        {
            return validation.Errors;
        }

        BatchNumber = batchNumber.Trim();
        Quantity = quantity;
        CostPrice = costPrice;
        Mrp = mrp;
        SalesPrice = salesPrice;
        MinSalePrice = minSalePrice;
        TaxRatePercent = taxRatePercent;
        ExpiryDate = expiryDate;
        ManufacturingDate = manufacturingDate;
        UpdatedBy = updatedBy;

        return Result.Success;
    }

    public decimal GetTaxAmountPerUnit()
    {
        return SalesPrice * (TaxRatePercent / 100m);
    }

    public void MarkUpdatedBy(Guid updatedBy)
    {
        UpdatedBy = updatedBy;
    }

    private static ErrorOr<Success> ValidateBatch(
        string batchNumber,
        decimal quantity,
        decimal salesPrice,
        decimal minSalePrice,
        decimal mrp,
        decimal taxRatePercent)
    {
        if (string.IsNullOrWhiteSpace(batchNumber))
        {
            return Error.Validation("InventoryBatch.BatchNumberRequired", "Batch number is required.");
        }

        if (quantity < 0)
        {
            return Error.Validation("InventoryBatch.QuantityNegative", "Quantity cannot be negative.");
        }

        if (taxRatePercent < 0 || taxRatePercent > 100)
        {
            return Error.Validation("InventoryBatch.TaxRateOutOfRange", "Tax rate must be between 0 and 100.");
        }

        if (minSalePrice > salesPrice)
        {
            return Error.Validation("InventoryBatch.MinSaleAboveSale", "Minimum sale price cannot exceed sales price.");
        }

        if (salesPrice > mrp)
        {
            return Error.Validation("InventoryBatch.SaleAboveMrp", "Sales price cannot exceed MRP.");
        }

        return Result.Success;
    }
}