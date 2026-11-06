using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

/// <summary>
/// The part of one <see cref="PaymentReceipt"/> applied to one customer invoice or supplier
/// purchase receipt. Immutable; it stops counting when its receipt is reversed.
/// </summary>
public sealed class PaymentAllocation : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid ReceiptId { get; private set; }
    public PaymentAllocationTargetType TargetType { get; private set; }
    public Guid TargetId { get; private set; }
    public decimal Amount { get; private set; }
    public Guid CreatedBy { get; private set; }

    private PaymentAllocation() { }

    public static ErrorOr<PaymentAllocation> Create(
        PaymentReceipt receipt,
        Guid targetId,
        decimal amount,
        Guid createdBy)
    {
        if (amount <= 0m || decimal.Round(amount, 2) != amount)
            return Error.Validation("Payment.AllocationAmountInvalid", "Allocation amount must be greater than zero with at most two decimals.");

        if (targetId == Guid.Empty)
            return Error.Validation("Payment.AllocationTargetRequired", "Allocation target is required.");

        return new PaymentAllocation
        {
            ShopId = receipt.ShopId,
            ReceiptId = receipt.Id,
            TargetType = receipt.AllocationTargetType,
            TargetId = targetId,
            Amount = amount,
            CreatedBy = createdBy,
        };
    }
}
