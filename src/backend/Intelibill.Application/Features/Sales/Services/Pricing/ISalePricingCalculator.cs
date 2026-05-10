using ErrorOr;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Services.Pricing;

public sealed record SalePricingCalculationRequest(
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
    decimal TotalAmount);

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
    decimal MaxAllowedItemDiscountPercent);

public interface ISalePricingCalculator
{
    ErrorOr<SalePricingCalculationResult> Calculate(SalePricingCalculationRequest request);
}

