using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.DTOs;

public sealed record SalePreviewDto(
    decimal TotalAmount,
    decimal TotalTaxableAmount,
    decimal TotalTaxAmount,
    decimal TotalDiscountAmount,
    decimal SaleLevelEligibleSubtotal,
    SalePreviewConfiguredSaleRuleDto? ConfiguredSaleRule,
    IReadOnlyList<SalePreviewLineDto> Lines,
    IReadOnlyList<SalePreviewInfoDto> Infos,
    IReadOnlyList<SalePreviewWarningDto> Warnings);

public sealed record SalePreviewConfiguredSaleRuleDto(
    Guid RuleId,
    string RuleType,
    decimal Percentage,
    decimal? ThresholdAmount);

public sealed record SalePreviewLineDto(
    SaleLineType LineType,
    Guid? ItemId,
    Guid? ServiceId,
    string Barcode,
    string ItemName,
    Guid? InventoryBatchId,
    string? BatchNumber,
    decimal Quantity,
    decimal CostPrice,
    decimal SalesPrice,
    decimal Mrp,
    decimal TaxRatePercent,
    bool IsPriceIncludingTax,
    decimal PreTaxAmountBeforeDiscount,
    decimal ItemDiscountAmount,
    decimal SaleDiscountAmount,
    decimal TaxableAmount,
    decimal TaxAmount,
    decimal LineTotalAmount,
    decimal MaxAllowedItemDiscountFlat,
    decimal MaxAllowedItemDiscountPercent,
    Guid? ConfiguredBatchRuleId,
    decimal? ConfiguredBatchRulePercentage,
    bool HasClientPriceMismatch,
    string? ClientLineKey)
{
    public SalePreviewLineDto(
        Guid itemId,
        string barcode,
        string itemName,
        Guid inventoryBatchId,
        string batchNumber,
        decimal quantity,
        decimal costPrice,
        decimal salesPrice,
        decimal mrp,
        decimal taxRatePercent,
        bool isPriceIncludingTax,
        decimal preTaxAmountBeforeDiscount,
        decimal itemDiscountAmount,
        decimal saleDiscountAmount,
        decimal taxableAmount,
        decimal taxAmount,
        decimal lineTotalAmount,
        decimal maxAllowedItemDiscountFlat,
        decimal maxAllowedItemDiscountPercent,
        Guid? configuredBatchRuleId,
        decimal? configuredBatchRulePercentage,
        bool hasClientPriceMismatch,
        string? clientLineKey)
        : this(SaleLineType.Goods, itemId, null, barcode, itemName, inventoryBatchId, batchNumber, quantity, costPrice, salesPrice, mrp, taxRatePercent, isPriceIncludingTax, preTaxAmountBeforeDiscount, itemDiscountAmount, saleDiscountAmount, taxableAmount, taxAmount, lineTotalAmount, maxAllowedItemDiscountFlat, maxAllowedItemDiscountPercent, configuredBatchRuleId, configuredBatchRulePercentage, hasClientPriceMismatch, clientLineKey)
    {
    }
}

public sealed record SalePreviewInfoDto(
    string Code,
    string Message);

public sealed record SalePreviewWarningDto(
    string Code,
    string Message,
    string Severity,
    Guid? InventoryBatchId,
    string? ClientLineKey);
