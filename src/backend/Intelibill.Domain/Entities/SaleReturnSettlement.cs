using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

/// <summary>
/// One posted settlement leg of a sale return: how much of the approved refund reduced a receivable,
/// was refunded outside the books or was issued as store credit, and the record it settled through.
/// Immutable once posted; a void reverses it by date, it is never edited.
/// </summary>
public sealed class SaleReturnSettlement : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid SaleReturnId { get; private set; }
    public int Sequence { get; private set; }
    public ReturnSettlementKind Kind { get; private set; }
    public decimal Amount { get; private set; }
    public Guid? CustomerId { get; private set; }
    public Guid? CreditNoteId { get; private set; }
    public Guid? CustomerLedgerEntryId { get; private set; }

    private SaleReturnSettlement() { }

    internal static SaleReturnSettlement Create(
        Guid shopId,
        Guid saleReturnId,
        int sequence,
        ReturnSettlementKind kind,
        decimal amount,
        Guid? customerId,
        Guid? creditNoteId,
        Guid? customerLedgerEntryId) =>
        new()
        {
            ShopId = shopId,
            SaleReturnId = saleReturnId,
            Sequence = sequence,
            Kind = kind,
            Amount = amount,
            CustomerId = customerId,
            CreditNoteId = creditNoteId,
            CustomerLedgerEntryId = customerLedgerEntryId,
        };
}
