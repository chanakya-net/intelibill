using Intelibill.Domain.Entities;
using Intelibill.Domain.Events;

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
            createdBy: Guid.NewGuid());

        Assert.Null(item.Description);
    }

    [Fact]
    public void Create_RaisesItemCreatedDomainEvent_WithCorrectProperties()
    {
        var shopId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();

        var item = Item.Create(shopId, "Rice", null, "kg", "BAR001", true, createdBy);

        var domainEvent = Assert.Single(item.DomainEvents);
        var created = Assert.IsType<ItemCreatedDomainEvent>(domainEvent);

        Assert.Equal(item.Id, created.ItemId);
        Assert.Equal("BAR001", created.Barcode);
        Assert.Equal("Rice", created.Name);
        Assert.Equal(shopId, created.ShopId);
        Assert.NotEqual(Guid.Empty, created.EventId);
        Assert.True(created.OccurredOn <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Update_DoesNotRaiseItemCreatedDomainEvent()
    {
        var item = Item.Create(Guid.NewGuid(), "Rice", null, "kg", "BAR001", true, Guid.NewGuid());
        item.ClearDomainEvents();

        item.Update("Rice Updated", null, "kg", "BAR001", true, Guid.NewGuid());

        Assert.Empty(item.DomainEvents);
    }
}