using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class CustomerTests
{
    [Fact]
    public void Create_TrimAndNormalizeFields()
    {
        var shopId = Guid.NewGuid();

        var customer = Customer.Create(
            shopId,
            "  John Doe  ",
            "  +919876543210  ",
            "  12 Market Road  ",
            true);

        Assert.Equal(shopId, customer.ShopId);
        Assert.Equal("John Doe", customer.Name);
        Assert.Equal("+919876543210", customer.PhoneNumber);
        Assert.Equal("12 Market Road", customer.Address);
        Assert.True(customer.IsActive);
        Assert.Equal(0m, customer.CreditLimit);
    }

    [Fact]
    public void Create_EmptyOptionalValues_NormalizesToNull()
    {
        var customer = Customer.Create(
            Guid.NewGuid(),
            "Customer",
            "+919876543210",
            "   ",
            true);

        Assert.Null(customer.Address);
    }

    [Fact]
    public void Create_WithCreditLimit_SetsCreditLimit()
    {
        var customer = Customer.Create(
            Guid.NewGuid(),
            "Customer",
            "+919876543210",
            null,
            true,
            500m);

        Assert.Equal(500m, customer.CreditLimit);
    }

    [Fact]
    public void Update_ChangesAllFields()
    {
        var customer = Customer.Create(
            Guid.NewGuid(),
            "Customer",
            "+919999999999",
            "Address",
            true);

        customer.Update(
            "  Updated Customer  ",
            "  +918888888888  ",
            "  42 MG Road  ",
            false,
            1000m);

        Assert.Equal("Updated Customer", customer.Name);
        Assert.Equal("+918888888888", customer.PhoneNumber);
        Assert.Equal("42 MG Road", customer.Address);
        Assert.False(customer.IsActive);
        Assert.Equal(1000m, customer.CreditLimit);
    }

    [Fact]
    public void Update_WithBlankAddress_NormalizesToNull()
    {
        var customer = Customer.Create(
            Guid.NewGuid(),
            "Customer",
            "+919999999999",
            "Original Address",
            true);

        customer.Update("Customer", "+919999999999", "   ", true, 0m);

        Assert.Null(customer.Address);
    }
}
