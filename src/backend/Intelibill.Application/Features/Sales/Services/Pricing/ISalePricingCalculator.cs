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
    Guid InventoryBatchId,
    decimal Quantity,
    decimal CostPrice,
    decimal SalesPrice,
    decimal Mrp,
    decimal TaxRatePercent,
    bool IsPriceIncludingTax,
    InstantDiscount ItemDiscount);

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
    decimal? ConfiguredBatchRulePercentage);

public interface ISalePricingCalculator
{
    Task<ErrorOr<SalePricingCalculationResult>> CalculateAsync(
        SalePricingCalculationRequest request,
        CancellationToken cancellationToken = default);
}
