using Intelibill.Domain.Enums;

namespace Intelibill.Domain.ValueObjects;

public sealed record SaleReturnLineInput(
    Guid ShopId,
    Guid SaleItemId,
    decimal Quantity,
    SaleReturnCondition? Condition,
    decimal OriginalCostPrice,
    decimal OriginalSalesPrice,
    decimal OriginalTaxRatePercent,
    bool OriginalIsPriceIncludingTax,
    decimal MaxRefundAmount,
    decimal ApprovedRefundAmount,
    decimal TaxableAmount,
    decimal TaxAmount,
    string? Notes);
