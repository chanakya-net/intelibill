using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class ShopTests
{
    [Fact]
    public void Create_TrimFields()
    {
        var shop = Shop.Create(
            "  Main Shop  ",
            "  42 MG Road  ",
            "  Bengaluru  ",
            "  Karnataka  ",
            "  560001  ",
            "  Chandra  ",
            "  9876543210  ");

        Assert.Equal("Main Shop", shop.Name);
        Assert.Equal("42 MG Road", shop.Address);
        Assert.Equal("Bengaluru", shop.City);
        Assert.Equal("Karnataka", shop.State);
        Assert.Equal("560001", shop.Pincode);
        Assert.Equal("Chandra", shop.ContactPerson);
        Assert.Equal("9876543210", shop.MobileNumber);
    }

    [Fact]
    public void Create_EmptyOptionalValues_NormalizesToNull()
    {
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", "   ", null);

        Assert.Null(shop.ContactPerson);
        Assert.Null(shop.MobileNumber);
    }

    [Fact]
    public void Rename_TrimName()
    {
        var shop = Shop.Create("Old", "Address", "City", "State", "560001", null, null);

        shop.Rename("  New Name  ");

        Assert.Equal("New Name", shop.Name);
    }

    [Fact]
    public void AddMembership_AttachesShopToMembership()
    {
        var user = User.CreateWithEmail("user@test.com", "hash", "First", "Last");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null);
        var membership = ShopMembership.Create(shop.Id, user.Id, ShopRole.Manager, false);

        shop.AddMembership(membership);

        var added = Assert.Single(shop.Memberships);
        Assert.Equal(shop.Id, added.Shop.Id);
        Assert.Equal(shop.Id, added.ShopId);
    }
}