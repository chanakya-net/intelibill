using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

public sealed class SaleReturnItem : BaseEntity
{
    public Guid SaleReturnId { get; private set; }
    public Guid ShopId { get; private set; }
    public Guid SaleId { get; private set; }
    public Guid SaleItemId { get; private set; }
    public decimal Quantity { get; private set; }
    public SaleReturnCondition Condition { get; private set; }
    public decimal OriginalCostPrice { get; private set; }
    public decimal OriginalSalesPrice { get; private set; }
    public decimal OriginalTaxRatePercent { get; private set; }
    public bool OriginalIsPriceIncludingTax { get; private set; }
    public decimal MaxRefundAmount { get; private set; }
    public decimal ApprovedRefundAmount { get; private set; }
    public decimal TaxableAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public string? Notes { get; private set; }

    private SaleReturnItem() { }

    public static ErrorOr<SaleReturnItem> Create(
        Guid shopId,
        Guid saleId,
        Guid saleItemId,
        decimal quantity,
        SaleReturnCondition condition,
        decimal originalCostPrice,
        decimal originalSalesPrice,
        decimal originalTaxRatePercent,
        bool originalIsPriceIncludingTax,
        decimal maxRefundAmount,
        decimal approvedRefundAmount,
        decimal taxableAmount,
        decimal taxAmount,
        string? notes)
    {
        if (quantity <= 0)
        {
            return Error.Validation("SaleReturnItem.QuantityMustBePositive", "Return quantity must be greater than zero.");
        }

        if (originalTaxRatePercent < 0 || originalTaxRatePercent > 100)
        {
            return Error.Validation("SaleReturnItem.TaxRateOutOfRange", "Tax rate must be between 0 and 100.");
        }

        if (originalCostPrice < 0 || originalSalesPrice < 0 || maxRefundAmount < 0 || approvedRefundAmount < 0 || taxableAmount < 0 || taxAmount < 0)
        {
            return Error.Validation("SaleReturnItem.AmountNegative", "Return line amounts cannot be negative.");
        }

        if (approvedRefundAmount > maxRefundAmount)
        {
            return Error.Validation("SaleReturnItem.RefundExceedsMax", "Approved refund cannot exceed max refund.");
        }

        return new SaleReturnItem
        {
            ShopId = shopId,
            SaleId = saleId,
            SaleItemId = saleItemId,
            Quantity = quantity,
            Condition = condition,
            OriginalCostPrice = originalCostPrice,
            OriginalSalesPrice = originalSalesPrice,
            OriginalTaxRatePercent = originalTaxRatePercent,
            OriginalIsPriceIncludingTax = originalIsPriceIncludingTax,
            MaxRefundAmount = maxRefundAmount,
            ApprovedRefundAmount = approvedRefundAmount,
            TaxableAmount = taxableAmount,
            TaxAmount = taxAmount,
            Notes = NormalizeOptional(notes),
        };
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
