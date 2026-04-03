using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class ItemTests
{
    [Fact]
    public void Create_TrimsAndNormalizesFields()
    {
        var item = Item.Create(
            Guid.NewGuid(),
            "  Atta  ",
            "  Whole wheat flour  ",
            "  KG  ",
            "  8900001234567  ",
            isActive: true,
            preferredSupplierId: Guid.NewGuid(),
            createdBy: Guid.NewGuid());

        Assert.Equal("Atta", item.Name);
        Assert.Equal("Whole wheat flour", item.Description);
        Assert.Equal("KG", item.Uom);
        Assert.Equal("8900001234567", item.Barcode);
        Assert.True(item.IsActive);
    }

    [Fact]
    public void Create_EmptyDescription_NormalizesToNull()
    {
        var item = Item.Create(
            Guid.NewGuid(),
            "Atta",
            "   ",
            "KG",
            "8900001234567",
            isActive: true,
            preferredSupplierId: null,
            createdBy: Guid.NewGuid());

        Assert.Null(item.Description);
    }
}