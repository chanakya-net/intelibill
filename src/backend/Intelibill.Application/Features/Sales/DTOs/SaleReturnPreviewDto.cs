using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.DTOs;

public sealed record SaleReturnPreviewDto(
    Guid SaleId,
    bool HasFinancialAccess,
    IReadOnlyList<SaleReturnPreviewLineDto> Lines,
    SaleReturnPreviewFinancialDto? Financial,
    IReadOnlyList<SaleReturnPreviewWarningDto> Warnings);

public sealed record SaleReturnPreviewLineDto(
    Guid SaleItemId,
    Guid? ItemId,
    Guid? InventoryBatchId,
    decimal RequestedQuantity,
    decimal ReturnedQuantity,
    decimal ReturnableQuantity,
    SaleReturnCondition? Condition,
    bool WillRestock,
    SaleReturnPreviewLineFinancialDto? Financial);

public sealed record SaleReturnPreviewLineFinancialDto(
    decimal OriginalCostPrice,
    decimal OriginalSalesPrice,
    decimal OriginalTaxRatePercent,
    bool OriginalIsPriceIncludingTax,
    decimal MaxRefundAmount,
    decimal ApprovedRefundAmount,
    decimal TaxableAmount,
    decimal TaxAmount);

public sealed record SaleReturnPreviewFinancialDto(
    decimal TotalRefundAmount,
    decimal DueReductionAmount,
    decimal PayoutAmount,
    decimal TotalTaxableAmount,
    decimal TotalTaxAmount,
    decimal? CustomerBalanceBefore,
    decimal? CustomerBalanceAfter);

public sealed record SaleReturnPreviewWarningDto(
    string Code,
    string Message,
    string Severity);
