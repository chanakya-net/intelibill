using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

public sealed class PurchaseOrderReceiptSequence : BaseEntity
{
    public Guid ShopId { get; private set; }
    public int Year { get; private set; }
    public int NextNumber { get; private set; }

    private PurchaseOrderReceiptSequence() { }

    public static PurchaseOrderReceiptSequence Create(Guid shopId, int year) =>
        new()
        {
            ShopId = shopId,
            Year = year,
            NextNumber = 1,
        };

    public int GetAndIncrement()
    {
        var current = NextNumber;
        NextNumber = checked(current + 1);
        return current;
    }
}
