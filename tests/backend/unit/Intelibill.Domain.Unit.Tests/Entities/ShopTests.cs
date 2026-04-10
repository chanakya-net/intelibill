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
            "  9876543210  ",
            "  27AAPFU0939F1ZV  ");

        Assert.Equal("Main Shop", shop.Name);
        Assert.Equal("42 MG Road", shop.Address);
        Assert.Equal("Bengaluru", shop.City);
        Assert.Equal("Karnataka", shop.State);
        Assert.Equal("560001", shop.Pincode);
        Assert.Equal("Chandra", shop.ContactPerson);
        Assert.Equal("9876543210", shop.MobileNumber);
        Assert.Equal("27AAPFU0939F1ZV", shop.GstNumber);
    }

    [Fact]
    public void Create_EmptyOptionalValues_NormalizesToNull()
    {
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", "   ", null, "   ");

        Assert.Null(shop.ContactPerson);
        Assert.Null(shop.MobileNumber);
        Assert.Null(shop.GstNumber);
    }

    [Fact]
    public void Rename_TrimName()
    {
        var shop = Shop.Create("Old", "Address", "City", "State", "560001", null, null, null);

        shop.Rename("  New Name  ");

        Assert.Equal("New Name", shop.Name);
    }

    [Fact]
    public void UpdateDetails_TrimFields()
    {
        var shop = Shop.Create("Old", "Old Address", "Old City", "Old State", "000000", null, null, null);

        shop.UpdateDetails(
            "  Main Shop  ",
            "  42 MG Road  ",
            "  Bengaluru  ",
            "  Karnataka  ",
            "  560001  ",
            "  Chandra  ",
            "  9876543210  ",
            "  29ABCDE1234F2Z5  ");

        Assert.Equal("Main Shop", shop.Name);
        Assert.Equal("42 MG Road", shop.Address);
        Assert.Equal("Bengaluru", shop.City);
        Assert.Equal("Karnataka", shop.State);
        Assert.Equal("560001", shop.Pincode);
        Assert.Equal("Chandra", shop.ContactPerson);
        Assert.Equal("9876543210", shop.MobileNumber);
        Assert.Equal("29ABCDE1234F2Z5", shop.GstNumber);
    }

    [Fact]
    public void UpdateDetails_EmptyOptionalValues_NormalizesToNull()
    {
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", "Chandra", "123", "27AAPFU0939F1ZV");

        shop.UpdateDetails("Main", "Address", "City", "State", "560001", "   ", null, "   ");

        Assert.Null(shop.ContactPerson);
        Assert.Null(shop.MobileNumber);
        Assert.Null(shop.GstNumber);
    }

    [Fact]
    public void UpdateBankDetails_SetsBankFields()
    {
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);

        shop.UpdateBankDetails("SBI", "123456789012", BankAccountType.Savings, "SBIN0001234", "  Chandra Kumar  ");

        Assert.Equal("SBI", shop.BankName);
        Assert.Equal("123456789012", shop.BankAccountNumber);
        Assert.Equal(BankAccountType.Savings, shop.BankAccountType);
        Assert.Equal("SBIN0001234", shop.IfscCode);
        Assert.Equal("Chandra Kumar", shop.AccountHolderName);
    }

    [Fact]
    public void UpdateBankDetails_EmptyValues_NormalizesToNull()
    {
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        shop.UpdateBankDetails("SBI", "123", BankAccountType.Current, "SBIN0001234", "Chandra");

        shop.UpdateBankDetails("   ", "  ", null, "  ", null);

        Assert.Null(shop.BankName);
        Assert.Null(shop.BankAccountNumber);
        Assert.Null(shop.BankAccountType);
        Assert.Null(shop.IfscCode);
        Assert.Null(shop.AccountHolderName);
    }

    [Fact]
    public void AddMembership_AttachesShopToMembership()
    {
        var user = User.CreateWithEmail("user@test.com", "hash", "First", "Last");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var membership = ShopMembership.Create(shop.Id, user.Id, ShopRole.Manager, false);

        shop.AddMembership(membership);

        var added = Assert.Single(shop.Memberships);
        Assert.Equal(shop.Id, added.Shop.Id);
        Assert.Equal(shop.Id, added.ShopId);
    }
}