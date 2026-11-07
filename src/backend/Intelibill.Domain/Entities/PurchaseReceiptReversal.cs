using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

public sealed class PurchaseReceiptReversal : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid ReceiptValueId { get; private set; }
    public Guid? PurchaseOrderReceiptId { get; private set; }
    public Guid CreatedBy { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset ReversedAt { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public string BusinessTimeZoneId { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal EffectiveAmount { get; private set; }
    public Guid StockTransactionId { get; private set; }
    public Guid? SupplierLedgerEntryId { get; private set; }
    public string Reference { get; private set; } = string.Empty;
    private PurchaseReceiptReversal() { }
    public static ErrorOr<PurchaseReceiptReversal> Create(Guid shopId, Guid receiptId, Guid? groupId, Guid actor, string reason, DateTimeOffset instant, DateOnly date, string zone, decimal quantity, decimal amount, Guid movementId, Guid? ledgerId, string reference)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 3 or > 255 || quantity <= 0 || amount < 0 || amount>PurchaseReceiptValue.MaxTotalCost || decimal.Round(amount,2)!=amount || (amount==0)!=(ledgerId is null))
            return Error.Validation("PurchaseReceipt.ReversalInvalid", "Reversal requires a reason and exact positive receipt quantity and nonnegative cost.");
        return new PurchaseReceiptReversal { ShopId=shopId, ReceiptValueId=receiptId, PurchaseOrderReceiptId=groupId, CreatedBy=actor, Reason=reason.Trim(), ReversedAt=instant.ToUniversalTime(), BusinessDate=date, BusinessTimeZoneId=zone, Quantity=quantity, EffectiveAmount=amount, StockTransactionId=movementId, SupplierLedgerEntryId=ledgerId, Reference=reference };
    }
}
