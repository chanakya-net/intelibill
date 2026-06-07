using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class PurchaseOrderSequenceTests
{
    [Fact]
    public void Create_SetsShopYearAndStartsAtOne()
    {
        var shopId = Guid.NewGuid();
        var seq = PurchaseOrderSequence.Create(shopId, 2026);

        Assert.Equal(shopId, seq.ShopId);
        Assert.Equal(2026, seq.Year);
        Assert.Equal(1, seq.NextNumber);
    }

    [Fact]
    public void GetAndIncrement_ReturnsCurrentAndAdvances()
    {
        var seq = PurchaseOrderSequence.Create(Guid.NewGuid(), 2026);

        var first = seq.GetAndIncrement();
        var second = seq.GetAndIncrement();
        var third = seq.GetAndIncrement();

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(3, third);
        Assert.Equal(4, seq.NextNumber);
    }
}
