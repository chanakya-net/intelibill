using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.Services.Returns;

public sealed record SaleReturnCalculationRequest(
    IReadOnlyList<SaleReturnLineCalculationRequest> Lines,
    decimal OutstandingDueAmount,
    decimal? CustomerBalanceBefore,
    decimal? DueReductionOverrideAmount = null,
    string? DueOverrideReason = null);

public sealed record SaleReturnLineCalculationRequest(
    Guid SaleItemId,
    decimal Quantity,
    decimal OriginalCostPrice,
    decimal OriginalSalesPrice,
    decimal OriginalTaxRatePercent,
    bool OriginalIsPriceIncludingTax,
    decimal OriginalSaleItemQuantity,
    decimal OriginalPaidTaxableAmount,
    decimal OriginalPaidTaxAmount,
    decimal OriginalPaidTotalAmount,
    SaleReturnCondition Condition,
    decimal? ApprovedRefundAmount = null,
    string? Notes = null);

public sealed record SaleReturnCalculationResult(
    IReadOnlyList<SaleReturnLineCalculation> Lines,
    decimal TotalRefundAmount,
    decimal DueReductionAmount,
    decimal PayoutAmount,
    decimal TotalTaxableAmount,
    decimal TotalTaxAmount,
    decimal? CustomerBalanceBefore,
    decimal? CustomerBalanceAfter,
    IReadOnlyList<SaleReturnCalculationWarning> Warnings);

public sealed record SaleReturnLineCalculation(
    Guid SaleItemId,
    decimal Quantity,
    SaleReturnCondition Condition,
    decimal OriginalCostPrice,
    decimal OriginalSalesPrice,
    decimal OriginalTaxRatePercent,
    bool OriginalIsPriceIncludingTax,
    decimal MaxRefundAmount,
    decimal ApprovedRefundAmount,
    decimal TaxableAmount,
    decimal TaxAmount,
    string? Notes);

public sealed record SaleReturnCalculationWarning(
    string Code,
    string Message,
    SaleReturnWarningSeverity Severity);

public enum SaleReturnWarningSeverity
{
    Info = 1,
    Warning = 2,
}

public interface ISaleReturnCalculator
{
    SaleReturnCalculationResult Calculate(SaleReturnCalculationRequest request);
}
