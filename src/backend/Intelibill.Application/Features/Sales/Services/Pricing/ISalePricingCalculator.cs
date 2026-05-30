using ErrorOr;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Services.Pricing;

public sealed record SalePricingCalculationRequest(
    Guid ShopId,
    DateTimeOffset SaleTime,
    IReadOnlyList<SalePricingLineCalculationRequest> Lines,
    InstantDiscount SaleDiscount);

public sealed record SalePricingLineCalculationRequest(
    SaleLineType LineType,
    Guid InventoryBatchId,
    Guid? ServiceId,
    decimal Quantity,
    decimal CostPrice,
    decimal SalesPrice,
    decimal Mrp,
    decimal TaxRatePercent,
    bool IsPriceIncludingTax,
    InstantDiscount ItemDiscount,
    bool IsSaleDiscountEligible)
{
    public SalePricingLineCalculationRequest(
        Guid InventoryBatchId,
        decimal Quantity,
        decimal CostPrice,
        decimal SalesPrice,
        decimal Mrp,
        decimal TaxRatePercent,
        bool IsPriceIncludingTax,
        InstantDiscount ItemDiscount)
        : this(SaleLineType.Goods, InventoryBatchId, null, Quantity, CostPrice, SalesPrice, Mrp, TaxRatePercent, IsPriceIncludingTax, ItemDiscount, true)
    {
    }
}

public sealed record SalePricingCalculationResult(
    IReadOnlyList<SalePricingLineCalculation> Lines,
    decimal SaleLevelEligibleSubtotal,
    decimal TotalTaxableAmount,
    decimal TotalTaxAmount,
    decimal TotalDiscountAmount,
    decimal TotalAmount,
    SalePricingConfiguredSaleRule? ConfiguredSaleRule,
    IReadOnlyList<SalePricingInfoMessage> Infos);

public sealed record SalePricingConfiguredSaleRule(
    Guid RuleId,
    DiscountRuleType RuleType,
    decimal Percentage,
    decimal? ThresholdAmount);

public sealed record SalePricingInfoMessage(
    string Code,
    string Message);

public sealed record SalePricingLineCalculation(
    SaleLineType LineType,
    Guid InventoryBatchId,
    Guid? ServiceId,
    decimal Quantity,
    decimal CostPrice,
    decimal SalesPrice,
    decimal TaxRatePercent,
    bool IsPriceIncludingTax,
    decimal PreTaxAmountBeforeDiscount,
    decimal ItemDiscountAmount,
    decimal SaleDiscountAmount,
    decimal TaxableAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    decimal MaxAllowedItemDiscountFlat,
    decimal MaxAllowedItemDiscountPercent,
    Guid? ConfiguredBatchRuleId,
    decimal? ConfiguredBatchRulePercentage)
{
    public SalePricingLineCalculation(
        Guid InventoryBatchId,
        decimal Quantity,
        decimal CostPrice,
        decimal SalesPrice,
        decimal TaxRatePercent,
        bool IsPriceIncludingTax,
        decimal PreTaxAmountBeforeDiscount,
        decimal ItemDiscountAmount,
        decimal SaleDiscountAmount,
        decimal TaxableAmount,
        decimal TaxAmount,
        decimal TotalAmount,
        decimal MaxAllowedItemDiscountFlat,
        decimal MaxAllowedItemDiscountPercent,
        Guid? ConfiguredBatchRuleId,
        decimal? ConfiguredBatchRulePercentage)
        : this(SaleLineType.Goods, InventoryBatchId, null, Quantity, CostPrice, SalesPrice, TaxRatePercent, IsPriceIncludingTax, PreTaxAmountBeforeDiscount, ItemDiscountAmount, SaleDiscountAmount, TaxableAmount, TaxAmount, TotalAmount, MaxAllowedItemDiscountFlat, MaxAllowedItemDiscountPercent, ConfiguredBatchRuleId, ConfiguredBatchRulePercentage)
    {
    }
}

public interface ISalePricingCalculator
{
    Task<ErrorOr<SalePricingCalculationResult>> CalculateAsync(
        SalePricingCalculationRequest request,
        CancellationToken cancellationToken = default);
}
