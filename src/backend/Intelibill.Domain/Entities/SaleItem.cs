using Intelibill.Domain.Common;
using Intelibill.Domain.ValueObjects;

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
    public decimal OriginalSalesPrice { get; private set; }
    public decimal FinalSalesPrice { get; private set; }
    public decimal PreTaxAmountBeforeDiscount { get; private set; }
    public decimal ItemDiscountAmount { get; private set; }
    public decimal SaleDiscountAmount { get; private set; }
    public decimal TaxableAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal TotalAmount { get; private set; }
    public Guid? ConfiguredBatchRuleId { get; private set; }
    public decimal? ConfiguredBatchRulePercentage { get; private set; }
    public InstantDiscountType ItemDiscountOverrideType { get; private set; }
    public decimal ItemDiscountOverrideValue { get; private set; }
    public string? HsnCode { get; private set; }

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
        bool hasPriceMismatch,
        decimal? originalSalesPrice = null,
        decimal? finalSalesPrice = null,
        decimal? preTaxAmountBeforeDiscount = null,
        decimal itemDiscountAmount = 0m,
        decimal saleDiscountAmount = 0m,
        decimal? taxableAmount = null,
        decimal? taxAmount = null,
        decimal? totalAmount = null,
        Guid? configuredBatchRuleId = null,
        decimal? configuredBatchRulePercentage = null,
        InstantDiscountType itemDiscountOverrideType = InstantDiscountType.None,
        decimal itemDiscountOverrideValue = 0m,
        string? hsnCode = null)
    {
        var originalUnitPrice = originalSalesPrice ?? salesPrice;
        var effectiveFinalUnitPrice = finalSalesPrice ?? originalUnitPrice;
        var effectivePreTaxAmountBeforeDiscount = preTaxAmountBeforeDiscount
            ?? CalculatePreTaxAmount(quantity, originalUnitPrice, taxRatePercent, isPriceIncludingTax);
        var effectiveTaxAmount = taxAmount
            ?? CalculateTaxAmount(quantity, effectiveFinalUnitPrice, taxRatePercent, isPriceIncludingTax, itemDiscountAmount, saleDiscountAmount);
        var effectiveTaxableAmount = taxableAmount
            ?? decimal.Round(
                effectivePreTaxAmountBeforeDiscount - itemDiscountAmount - saleDiscountAmount,
                2,
                MidpointRounding.AwayFromZero);
        var effectiveTotalAmount = totalAmount
            ?? CalculateTotalAmount(quantity, effectiveFinalUnitPrice, taxRatePercent, isPriceIncludingTax, itemDiscountAmount, saleDiscountAmount);

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
            OriginalSalesPrice = originalUnitPrice,
            FinalSalesPrice = effectiveFinalUnitPrice,
            PreTaxAmountBeforeDiscount = effectivePreTaxAmountBeforeDiscount,
            ItemDiscountAmount = itemDiscountAmount,
            SaleDiscountAmount = saleDiscountAmount,
            TaxableAmount = effectiveTaxableAmount,
            TaxAmount = effectiveTaxAmount,
            TotalAmount = effectiveTotalAmount,
            ConfiguredBatchRuleId = configuredBatchRuleId,
            ConfiguredBatchRulePercentage = configuredBatchRulePercentage,
            ItemDiscountOverrideType = itemDiscountOverrideType,
            ItemDiscountOverrideValue = itemDiscountOverrideValue,
            HsnCode = NormalizeHsnCode(hsnCode),
        };
    }

    private static string? NormalizeHsnCode(string? hsnCode) =>
        string.IsNullOrWhiteSpace(hsnCode) ? null : hsnCode.Trim();

    private static decimal CalculatePreTaxAmount(
        decimal quantity,
        decimal unitPrice,
        decimal taxRatePercent,
        bool isPriceIncludingTax)
    {
        var grossAmount = quantity * unitPrice;
        if (!isPriceIncludingTax || taxRatePercent <= 0m)
        {
            return decimal.Round(grossAmount, 2, MidpointRounding.AwayFromZero);
        }

        var taxAmount = grossAmount * taxRatePercent / (100m + taxRatePercent);
        return decimal.Round(grossAmount - taxAmount, 2, MidpointRounding.AwayFromZero);
    }

    private static decimal CalculateTaxAmount(
        decimal quantity,
        decimal unitPrice,
        decimal taxRatePercent,
        bool isPriceIncludingTax,
        decimal itemDiscountAmount,
        decimal saleDiscountAmount)
    {
        if (taxRatePercent <= 0m)
        {
            return 0m;
        }

        var grossAmount = quantity * unitPrice;
        if (isPriceIncludingTax)
        {
            var discountedGrossAmount = decimal.Round(
                grossAmount - itemDiscountAmount - saleDiscountAmount,
                2,
                MidpointRounding.AwayFromZero);
            return decimal.Round(
                discountedGrossAmount * taxRatePercent / (100m + taxRatePercent),
                2,
                MidpointRounding.AwayFromZero);
        }

        var taxableAmount = decimal.Round(
            CalculatePreTaxAmount(quantity, unitPrice, taxRatePercent, isPriceIncludingTax) - itemDiscountAmount - saleDiscountAmount,
            2,
            MidpointRounding.AwayFromZero);
        return decimal.Round(taxableAmount * taxRatePercent / 100m, 2, MidpointRounding.AwayFromZero);
    }

    private static decimal CalculateTotalAmount(
        decimal quantity,
        decimal unitPrice,
        decimal taxRatePercent,
        bool isPriceIncludingTax,
        decimal itemDiscountAmount,
        decimal saleDiscountAmount)
    {
        var grossAmount = decimal.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero);
        if (isPriceIncludingTax)
        {
            return decimal.Round(grossAmount - itemDiscountAmount - saleDiscountAmount, 2, MidpointRounding.AwayFromZero);
        }

        var taxableAmount = decimal.Round(
            CalculatePreTaxAmount(quantity, unitPrice, taxRatePercent, isPriceIncludingTax) - itemDiscountAmount - saleDiscountAmount,
            2,
            MidpointRounding.AwayFromZero);
        var taxAmount = decimal.Round(taxableAmount * taxRatePercent / 100m, 2, MidpointRounding.AwayFromZero);
        return decimal.Round(taxableAmount + taxAmount, 2, MidpointRounding.AwayFromZero);
    }
}
