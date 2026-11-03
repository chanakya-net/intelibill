using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Entities;

public sealed class SaleReturn : BaseEntity
{
    private readonly List<SaleReturnItem> _items = [];
    private readonly List<SaleReturnSettlement> _settlements = [];

    public Guid ShopId { get; private set; }
    public Guid SaleId { get; private set; }
    public string ReturnNumber { get; private set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; private set; }
    public Guid ProcessedBy { get; private set; }
    public string? Notes { get; private set; }
    public decimal TotalRefundAmount { get; private set; }
    public decimal DueReductionAmount { get; private set; }
    public decimal PayoutAmount { get; private set; }
    public ReturnPayoutDestination? PayoutDestination { get; private set; }
    public decimal TotalTaxableAmount { get; private set; }
    public decimal TotalTaxAmount { get; private set; }
    public decimal? CustomerBalanceBefore { get; private set; }
    public decimal? CustomerBalanceAfter { get; private set; }
    public bool IsVoided { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public Guid? VoidedBy { get; private set; }
    public string? VoidReason { get; private set; }

    // The posted accounting snapshot. Null until posted; legacy rows carry what the backfill could
    // evidence, with NeedsReview where it could not.
    public ReturnAccountingStatus? AccountingStatus { get; private set; }
    public string? AccountingPolicyVersion { get; private set; }
    public string? AccountingReviewReason { get; private set; }
    public decimal? AdjustmentTaxableAmount { get; private set; }
    public decimal? AdjustmentTaxAmount { get; private set; }
    public decimal? AdjustmentNonTaxAmount { get; private set; }

    public IReadOnlyList<SaleReturnItem> Items => _items.AsReadOnly();
    public IReadOnlyList<SaleReturnSettlement> Settlements => _settlements.AsReadOnly();

    /// <summary>The paid invoice value of what came back, whatever was refunded for it.</summary>
    public decimal OriginalReturnedGross => _items.Sum(i => i.MaxRefundAmount);

    private SaleReturn() { }

    public static ErrorOr<SaleReturn> Record(
        Guid shopId,
        Guid saleId,
        string returnNumber,
        DateTimeOffset processedAt,
        Guid processedBy,
        string? notes,
        decimal totalRefundAmount,
        decimal dueReductionAmount,
        decimal payoutAmount,
        ReturnPayoutDestination? payoutDestination,
        decimal totalTaxableAmount,
        decimal totalTaxAmount,
        decimal? customerBalanceBefore,
        decimal? customerBalanceAfter,
        IReadOnlyList<SaleReturnLineInput> lines)
    {
        if (string.IsNullOrWhiteSpace(returnNumber))
        {
            return Error.Validation("SaleReturn.ReturnNumberRequired", "Return number is required.");
        }

        if (totalRefundAmount < 0 || dueReductionAmount < 0 || payoutAmount < 0 || totalTaxableAmount < 0 || totalTaxAmount < 0)
        {
            return Error.Validation("SaleReturn.AmountNegative", "Return amounts cannot be negative.");
        }

        if (dueReductionAmount + payoutAmount != totalRefundAmount)
        {
            return Error.Validation("SaleReturn.RefundSplitMismatch", "Due reduction plus payout must equal total refund.");
        }

        var noteValidation = ValidateLineNotes(lines);
        if (noteValidation.IsError)
        {
            return noteValidation.Errors;
        }

        var payoutValidation = ValidatePayoutDestination(payoutAmount, payoutDestination);
        if (payoutValidation.IsError)
        {
            return payoutValidation.Errors;
        }

        var items = new List<SaleReturnItem>(lines.Count);
        foreach (var line in lines)
        {
            var itemResult = SaleReturnItem.Create(
                line.ShopId,
                saleId,
                line.SaleItemId,
                line.Quantity,
                line.Condition,
                line.OriginalCostPrice,
                line.OriginalSalesPrice,
                line.OriginalTaxRatePercent,
                line.OriginalIsPriceIncludingTax,
                line.MaxRefundAmount,
                line.ApprovedRefundAmount,
                line.TaxableAmount,
                line.TaxAmount,
                NormalizeOptional(line.Notes));

            if (itemResult.IsError)
            {
                return itemResult.Errors;
            }

            items.Add(itemResult.Value);
        }

        var saleReturn = new SaleReturn
        {
            ShopId = shopId,
            SaleId = saleId,
            ReturnNumber = returnNumber.Trim(),
            ProcessedAt = processedAt,
            ProcessedBy = processedBy,
            Notes = NormalizeOptional(notes),
            TotalRefundAmount = totalRefundAmount,
            DueReductionAmount = dueReductionAmount,
            PayoutAmount = payoutAmount,
            PayoutDestination = payoutDestination,
            TotalTaxableAmount = totalTaxableAmount,
            TotalTaxAmount = totalTaxAmount,
            CustomerBalanceBefore = customerBalanceBefore,
            CustomerBalanceAfter = customerBalanceAfter,
            IsVoided = false,
        };

        saleReturn._items.AddRange(items);
        return saleReturn;
    }

    public ErrorOr<Success> Void(DateTimeOffset voidedAt, Guid voidedBy, string reason)
    {
        if (IsVoided)
        {
            return Error.Validation("SaleReturn.AlreadyVoided", "Sale return is already voided.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("SaleReturn.VoidReasonRequired", "Void reason is required.");
        }

        IsVoided = true;
        VoidedAt = voidedAt;
        VoidedBy = voidedBy;
        VoidReason = reason.Trim();

        return Result.Success;
    }

    /// <summary>
    /// Posts the financial side of this return once: every line's split must add up to its approved
    /// refund, the receivable leg must equal the due reduction and the payout legs the payout, each
    /// through the destination the return recorded.
    /// </summary>
    public ErrorOr<Success> PostAccounting(ReturnAccountingSnapshot snapshot)
    {
        if (AccountingStatus.HasValue)
            return Errors.Sale.ReturnAccountingAlreadyPosted;

        if (IsVoided)
            return Errors.Sale.ReturnAccountingVoided;

        var itemsBySaleItemId = _items.ToDictionary(i => i.SaleItemId);
        if (snapshot.Lines.Count != itemsBySaleItemId.Count
            || snapshot.Lines.Any(line =>
                !itemsBySaleItemId.TryGetValue(line.SaleItemId, out var item)
                || line.Total != item.ApprovedRefundAmount))
        {
            return Errors.Sale.ReturnAccountingLineMismatch;
        }

        var settlementValidation = ValidateSettlements(snapshot.Settlements);
        if (settlementValidation.IsError)
            return settlementValidation.Errors;

        foreach (var line in snapshot.Lines)
            itemsBySaleItemId[line.SaleItemId].PostAccounting(line);

        var sequence = 1;
        foreach (var settlement in snapshot.Settlements)
        {
            _settlements.Add(SaleReturnSettlement.Create(
                ShopId,
                Id,
                sequence++,
                settlement.Kind,
                settlement.Amount,
                settlement.CustomerId,
                settlement.CreditNoteId,
                settlement.CustomerLedgerEntryId));
        }

        AccountingStatus = ReturnAccountingStatus.Balanced;
        AccountingPolicyVersion = snapshot.PolicyVersion;
        AccountingReviewReason = null;
        AdjustmentTaxableAmount = snapshot.TaxableAmount;
        AdjustmentTaxAmount = snapshot.TaxAmount;
        AdjustmentNonTaxAmount = snapshot.NonTaxAmount;

        return Result.Success;
    }

    private ErrorOr<Success> ValidateSettlements(IReadOnlyList<ReturnSettlementInput> settlements)
    {
        var receivable = settlements
            .Where(s => s.Kind == ReturnSettlementKind.ReceivableReduction)
            .Sum(s => s.Amount);
        var payout = settlements
            .Where(s => s.Kind != ReturnSettlementKind.ReceivableReduction)
            .Sum(s => s.Amount);

        if (receivable != DueReductionAmount || payout != PayoutAmount)
            return Errors.Sale.ReturnAccountingSettlementMismatch;

        foreach (var settlement in settlements)
        {
            var destinationMatches = settlement.Kind switch
            {
                ReturnSettlementKind.ReceivableReduction => true,
                ReturnSettlementKind.StoreCreditIssued => PayoutDestination == ReturnPayoutDestination.CreditNote,
                ReturnSettlementKind.ExternalRefund => PayoutDestination == ReturnPayoutDestination.Refund,
                _ => false,
            };

            if (!destinationMatches)
                return Errors.Sale.ReturnAccountingSettlementInvalid;
        }

        return Result.Success;
    }

    private static ErrorOr<Success> ValidateLineNotes(IReadOnlyList<SaleReturnLineInput> lines)
    {
        foreach (var line in lines)
        {
            if (!string.IsNullOrWhiteSpace(line.Notes))
            {
                continue;
            }

            if (line.Condition == SaleReturnCondition.Wastage)
            {
                return Errors.Sale.ReturnNoteRequired("wastage returns");
            }

            if (line.ApprovedRefundAmount == 0m)
            {
                return Errors.Sale.ReturnNoteRequired("zero refunds");
            }

            if (line.ApprovedRefundAmount < line.MaxRefundAmount)
            {
                return Errors.Sale.ReturnNoteRequired("partial refunds");
            }
        }

        return Result.Success;
    }

    private static ErrorOr<Success> ValidatePayoutDestination(decimal payoutAmount, ReturnPayoutDestination? payoutDestination)
    {
        if (payoutAmount <= 0m)
        {
            return Result.Success;
        }

        if (!payoutDestination.HasValue)
        {
            return Errors.Sale.ReturnPayoutDestinationRequired;
        }

        return Enum.IsDefined(payoutDestination.Value)
            ? Result.Success
            : Errors.Sale.ReturnPayoutDestinationInvalid;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
