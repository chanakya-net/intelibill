using ErrorOr;
using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

public sealed class CreditNoteRedemption : BaseEntity
{
    public Guid CreditNoteId { get; private set; }
    public Guid ShopId { get; private set; }
    public Guid SaleId { get; private set; }
    public decimal Amount { get; private set; }

    private CreditNoteRedemption() { }

    internal static ErrorOr<CreditNoteRedemption> Create(
        Guid creditNoteId,
        Guid shopId,
        Guid saleId,
        decimal amount)
    {
        if (amount <= 0)
        {
            return Error.Validation("CreditNoteRedemption.AmountMustBePositive", "Redemption amount must be greater than zero.");
        }

        return new CreditNoteRedemption
        {
            CreditNoteId = creditNoteId,
            ShopId = shopId,
            SaleId = saleId,
            Amount = amount,
        };
    }
}
