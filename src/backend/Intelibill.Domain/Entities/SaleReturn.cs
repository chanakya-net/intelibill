using ErrorOr;
using Intelibill.Domain.Common;

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

    public static ErrorOr<SaleReturn> Create(
        Guid shopId,
        Guid saleId,
        string returnNumber,
        DateTimeOffset processedAt,
        Guid processedBy,
        string? notes,
        decimal totalRefundAmount,
        decimal dueReductionAmount,
        decimal payoutAmount,
        decimal totalTaxableAmount,
        decimal totalTaxAmount,
        decimal? customerBalanceBefore,
        decimal? customerBalanceAfter,
        IReadOnlyList<SaleReturnItem> items)
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

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
