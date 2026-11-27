using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

public sealed class SupplierReturnCreditLine : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid SupplierReturnCreditId { get; private set; }
    public Guid ReturnLineId { get; private set; }
    public Guid ReceiptValueId { get; private set; }
    public decimal AcceptedAmount { get; private set; }
    public Guid? SupplierLedgerEntryId { get; private set; }
    private SupplierReturnCreditLine() { }
    public static ErrorOr<SupplierReturnCreditLine> Create(Guid shopId, Guid creditId, Guid lineId, Guid receiptId, decimal amount, Guid? ledgerId)
    {
        if (amount < 0 || amount > PurchaseReceiptValue.MaxTotalCost || decimal.Round(amount,2)!=amount || (amount==0) != (ledgerId is null))
            return Error.Validation("SupplierReturn.CreditInvalid", "Positive credit requires a linked ledger entry; zero credit has none.");
        return new SupplierReturnCreditLine { ShopId=shopId, SupplierReturnCreditId=creditId, ReturnLineId=lineId, ReceiptValueId=receiptId, AcceptedAmount=amount, SupplierLedgerEntryId=ledgerId };
    }
}
