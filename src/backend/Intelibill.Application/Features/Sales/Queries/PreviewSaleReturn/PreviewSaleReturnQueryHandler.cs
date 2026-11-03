using ErrorOr;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Services.Returns;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.Queries.PreviewSaleReturn;

public sealed class PreviewSaleReturnQueryHandler(
    ISaleReturnValidator saleReturnValidator,
    IReturnAccountingPolicy returnAccountingPolicy)
{
    public async Task<ErrorOr<SaleReturnPreviewDto>> Handle(
        PreviewSaleReturnQuery query,
        CancellationToken cancellationToken)
    {
        var validation = await saleReturnValidator.ValidateAsync(
            new SaleReturnValidationRequest(
                query.ActorUserId,
                query.ShopId,
                query.SaleId,
                query.DueReductionOverrideAmount,
                query.DueOverrideReason,
                query.Items.Select(i => new SaleReturnValidationLineRequest(
                    i.SaleItemId,
                    i.Quantity,
                    i.LineType,
                    i.Condition,
                    i.ApprovedRefundAmount,
                    i.Notes)).ToList()),
            cancellationToken);

        if (validation.IsError)
            return validation.Errors;

        var validated = validation.Value;
        var hasFinancialAccess = validated.Membership.Role is ShopRole.Owner or ShopRole.Manager;
        var calculationsBySaleItemId = validated.Calculation.Lines.ToDictionary(line => line.SaleItemId);

        return new SaleReturnPreviewDto(
            validated.Sale.Id,
            hasFinancialAccess,
            validated.Lines.Select(line =>
            {
                var calculated = calculationsBySaleItemId[line.Request.SaleItemId];
                return new SaleReturnPreviewLineDto(
                    line.Request.SaleItemId,
                    line.SaleItem.ItemId,
                    line.SaleItem.InventoryBatchId,
                    line.Request.Quantity,
                    line.ReturnedQuantity,
                    line.ReturnableQuantity,
                    line.Request.Condition,
                    line.Request.Condition == SaleReturnCondition.Restockable,
                    hasFinancialAccess ? new SaleReturnPreviewLineFinancialDto(
                        calculated.OriginalCostPrice,
                        calculated.OriginalSalesPrice,
                        calculated.OriginalTaxRatePercent,
                        calculated.OriginalIsPriceIncludingTax,
                        calculated.MaxRefundAmount,
                        calculated.ApprovedRefundAmount,
                        calculated.TaxableAmount,
                        calculated.TaxAmount) : null);
            }).ToList(),
            hasFinancialAccess ? new SaleReturnPreviewFinancialDto(
                validated.Calculation.TotalRefundAmount,
                validated.Calculation.DueReductionAmount,
                validated.Calculation.PayoutAmount,
                validated.Calculation.TotalTaxableAmount,
                validated.Calculation.TotalTaxAmount,
                validated.Calculation.CustomerBalanceBefore,
                validated.Calculation.CustomerBalanceAfter) : null,
            FilterWarnings(WithAccountingWarning(validated.Calculation), hasFinancialAccess));
    }

    /// <summary>Posting refuses a refund the accounting policy cannot classify; the preview says so first.</summary>
    private IReadOnlyList<SaleReturnCalculationWarning> WithAccountingWarning(SaleReturnCalculationResult calculation)
    {
        var classification = returnAccountingPolicy.Classify(calculation.Lines
            .Select(line => new ReturnAccountingLineRequest(
                line.SaleItemId,
                line.MaxRefundAmount,
                line.TaxAmount,
                line.ApprovedRefundAmount))
            .ToList());

        if (!classification.IsError)
            return calculation.Warnings;

        return
        [
            .. calculation.Warnings,
            new SaleReturnCalculationWarning(
                "sale_return.accounting_unsupported",
                classification.FirstError.Description,
                SaleReturnWarningSeverity.Warning),
        ];
    }

    private static List<SaleReturnPreviewWarningDto> FilterWarnings(
        IReadOnlyList<SaleReturnCalculationWarning> warnings,
        bool hasFinancialAccess)
    {
        var visibleWarnings = hasFinancialAccess
            ? warnings
            : warnings.Where(w => w.Code == "sale_return.note_required.wastage");

        return visibleWarnings
            .Select(w => new SaleReturnPreviewWarningDto(w.Code, w.Message, w.Severity.ToString()))
            .ToList();
    }

}
