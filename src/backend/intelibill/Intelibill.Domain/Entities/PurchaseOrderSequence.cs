using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

public sealed class PurchaseOrderSequence : BaseEntity
{
    public Guid ShopId { get; private set; }
    public int Year { get; private set; }
    public int NextNumber { get; private set; }

    private PurchaseOrderSequence() { }

    public static PurchaseOrderSequence Create(Guid shopId, int year)
    {
        return new PurchaseOrderSequence
        {
            ShopId = shopId,
            Year = year,
            NextNumber = 1,
        };
    }

    public int GetAndIncrement()
    {
        var current = NextNumber;
        NextNumber = checked(current + 1);
        return current;
    }
}
