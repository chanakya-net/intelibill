using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Entities;

public sealed class SaleReturn : BaseEntity
{
    private readonly List<SaleReturnItem> _items = [];

    public Guid ShopId { get; private set; }
    public Guid SaleId { get; private set; }
    public string ReturnNumber { get; private set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; private set; }
    public Guid ProcessedBy { get; private set; }
    public string? Notes { get; private set; }
    public decimal TotalRefundAmount { get; private set; }
    public decimal DueReductionAmount { get; private set; }
    public decimal PayoutAmount { get; private set; }
    public decimal TotalTaxableAmount { get; private set; }
    public decimal TotalTaxAmount { get; private set; }
    public decimal? CustomerBalanceBefore { get; private set; }
    public decimal? CustomerBalanceAfter { get; private set; }
    public bool IsVoided { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public Guid? VoidedBy { get; private set; }
    public string? VoidReason { get; private set; }

    public IReadOnlyList<SaleReturnItem> Items => _items.AsReadOnly();

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
        PaymentMethod? payoutMethod,
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

        var payoutValidation = ValidatePayoutMethod(payoutAmount, payoutMethod);
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

    private static ErrorOr<Success> ValidatePayoutMethod(decimal payoutAmount, PaymentMethod? payoutMethod)
    {
        if (payoutAmount <= 0m)
        {
            return Result.Success;
        }

        if (!payoutMethod.HasValue)
        {
            return Errors.Sale.ReturnPayoutMethodRequired;
        }

        return payoutMethod.Value is PaymentMethod.Cash or PaymentMethod.UPI or PaymentMethod.Card
            ? Result.Success
            : Errors.Sale.ReturnPayoutMethodInvalid;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
