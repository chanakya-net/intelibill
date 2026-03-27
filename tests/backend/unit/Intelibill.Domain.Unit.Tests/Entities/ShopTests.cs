using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class ShopTests
{
    [Fact]
    public void Create_TrimName()
    {
        var shop = Shop.Create("  Main Shop  ");

        Assert.Equal("Main Shop", shop.Name);
    }

    [Fact]
    public void Rename_TrimName()
    {
        var shop = Shop.Create("Old");

        shop.Rename("  New Name  ");

        Assert.Equal("New Name", shop.Name);
    }

    [Fact]
    public void AddMembership_AttachesShopToMembership()
    {
        var user = User.CreateWithEmail("user@test.com", "hash", "First", "Last");
        var shop = Shop.Create("Main");
        var membership = ShopMembership.Create(shop.Id, user.Id, ShopRole.Manager, false);

        shop.AddMembership(membership);

        var added = Assert.Single(shop.Memberships);
        Assert.Equal(shop.Id, added.Shop.Id);
        Assert.Equal(shop.Id, added.ShopId);
    }
}