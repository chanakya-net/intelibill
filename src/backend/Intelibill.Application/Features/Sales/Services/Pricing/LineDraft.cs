namespace Intelibill.Application.Features.Sales.Services.Pricing;

internal sealed record LineDraft(
    int LineIndex,
    Intelibill.Domain.Enums.SaleLineType LineType,
    Guid InventoryBatchId,
    Guid? ServiceId,
    decimal Quantity,
    decimal CostPrice,
    decimal SalesPrice,
    decimal TaxRatePercent,
    bool IsPriceIncludingTax,
    decimal PreTaxBeforeDiscount,
    decimal CostTotal,
    decimal ItemDiscountAmount,
    decimal TaxableAfterItemDiscount,
    decimal SaleDiscountCapacity,
    decimal MaxAllowedItemDiscountFlat,
    decimal MaxAllowedItemDiscountPercent,
    Guid? ConfiguredBatchRuleId,
    decimal? ConfiguredBatchRulePercentage,
    bool IsSaleDiscountEligible)
{
    public bool EligibleForSaleDiscount => IsSaleDiscountEligible && SaleDiscountCapacity > 0m && TaxableAfterItemDiscount > 0m;
}
