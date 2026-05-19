using Intelibill.Domain.Enums;

namespace Intelibill.Domain.ValueObjects;

public sealed record SaleLineInput(
    Guid ShopId,
    Guid ItemId,
    Guid InventoryBatchId,
    decimal Quantity,
    decimal CostPrice,
    decimal SalesPrice,
    decimal Mrp,
    decimal TaxRatePercent,
    bool IsPriceIncludingTax,
    bool HasPriceMismatch,
    decimal? PreTaxAmountBeforeDiscount = null,
    decimal ItemDiscountAmount = 0m,
    decimal SaleDiscountAmount = 0m,
    decimal? TaxableAmount = null,
    decimal? TaxAmount = null,
    decimal? TotalAmount = null,
    Guid? ConfiguredBatchRuleId = null,
    decimal? ConfiguredBatchRulePercentage = null,
    InstantDiscountType ItemDiscountOverrideType = InstantDiscountType.None,
    decimal ItemDiscountOverrideValue = 0m,
    string? HsnCode = null);
