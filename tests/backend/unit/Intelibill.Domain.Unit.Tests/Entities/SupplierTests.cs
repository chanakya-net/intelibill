using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class SupplierTests
{
    [Fact]
    public void Create_TrimAndNormalizeFields()
    {
        var shopId = Guid.NewGuid();

        var supplier = Supplier.Create(
            shopId,
            "  Fresh Foods Pvt Ltd  ",
            "  Ramesh Kumar  ",
            "  +919876543210  ",
            "  12 Market Road  ",
            "  Bengaluru  ",
            "  Karnataka  ",
            "  560001  ",
            true,
            false);

        Assert.Equal(shopId, supplier.ShopId);
        Assert.Equal("Fresh Foods Pvt Ltd", supplier.Name);
        Assert.Equal("Ramesh Kumar", supplier.ContactPersonName);
        Assert.Equal("+919876543210", supplier.ContactPersonPhone);
        Assert.Equal("12 Market Road", supplier.Address);
        Assert.Equal("Bengaluru", supplier.City);
        Assert.Equal("Karnataka", supplier.State);
        Assert.Equal("560001", supplier.Pin);
        Assert.True(supplier.IsActive);
        Assert.False(supplier.IsPreferred);
    }

    [Fact]
    public void Create_EmptyOptionalValues_NormalizesToNull()
    {
        var supplier = Supplier.Create(
            Guid.NewGuid(),
            "Supplier",
            "   ",
            null,
            "Address",
            "City",
            "State",
            "560001",
            true,
            true);

        Assert.Null(supplier.ContactPersonName);
        Assert.Null(supplier.ContactPersonPhone);
    }

    [Fact]
    public void Update_ChangesAllFields()
    {
        var supplier = Supplier.Create(
            Guid.NewGuid(),
            "Supplier",
            "Name",
            "+919999999999",
            "Address",
            "City",
            "State",
            "560001",
            true,
            false);

        supplier.Update(
            "  Updated Supplier  ",
            "  New Contact  ",
            "  +918888888888  ",
            "  42 MG Road  ",
            "  Mysuru  ",
            "  Karnataka  ",
            "  570001  ",
            false,
            true);

        Assert.Equal("Updated Supplier", supplier.Name);
        Assert.Equal("New Contact", supplier.ContactPersonName);
        Assert.Equal("+918888888888", supplier.ContactPersonPhone);
        Assert.Equal("42 MG Road", supplier.Address);
        Assert.Equal("Mysuru", supplier.City);
        Assert.Equal("Karnataka", supplier.State);
        Assert.Equal("570001", supplier.Pin);
        Assert.False(supplier.IsActive);
        Assert.True(supplier.IsPreferred);
    }
}
