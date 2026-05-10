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
    Guid ItemId,
    string Barcode,
    string ItemName,
    Guid InventoryBatchId,
    string BatchNumber,
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
    string? ClientLineKey);

public sealed record SalePreviewInfoDto(
    string Code,
    string Message);

public sealed record SalePreviewWarningDto(
    string Code,
    string Message,
    string Severity,
    Guid InventoryBatchId,
    string? ClientLineKey);

