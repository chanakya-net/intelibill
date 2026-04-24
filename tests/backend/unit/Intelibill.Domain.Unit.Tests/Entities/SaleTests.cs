using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class SaleTests
{
    [Fact]
    public void Create_WithValidData_SetsSaleProperties()
    {
        var shopId = Guid.NewGuid();
        var invoiceNumber = "INV-20260416-ABCD1234";
        var customerId = Guid.NewGuid();
        var soldAt = DateTimeOffset.UtcNow;
        var items = new List<SaleItem>();

        var sale = Sale.Create(shopId, invoiceNumber, customerId, null, null,
            PaymentMethod.Cash, soldAt, 500m, 0m, 500m, 45m, items);

        Assert.Equal(shopId, sale.ShopId);
        Assert.Equal(invoiceNumber, sale.InvoiceNumber);
        Assert.Equal(customerId, sale.CustomerId);
        Assert.Equal(PaymentMethod.Cash, sale.PaymentMethod);
        Assert.Equal(soldAt, sale.SoldAt);
        Assert.Equal(500m, sale.TotalAmount);
        Assert.Equal(45m, sale.TotalTaxAmount);
    }

    [Fact]
    public void Create_WithWalkInCustomer_StoresNameAndPhone()
    {
        var sale = Sale.Create(Guid.NewGuid(), "INV-20260416-ABCD1234",
            null, "  Ravi Kumar  ", "  +919876543210  ",
            PaymentMethod.UPI, DateTimeOffset.UtcNow, 200m, 0m, 200m, 18m, []);

        Assert.Null(sale.CustomerId);
        Assert.Equal("Ravi Kumar", sale.CustomerName);
        Assert.Equal("+919876543210", sale.CustomerPhone);
    }

    [Fact]
    public void Create_WithItems_AttachesItemsToSale()
    {
        var shopId = Guid.NewGuid();
        var saleItem = SaleItem.Create(shopId, Guid.NewGuid(), Guid.NewGuid(),
            5m, 80m, 100m, 120m, 18m, false, false);

        var sale = Sale.Create(shopId, "INV-20260416-ABCD1234",
            null, null, null, PaymentMethod.Card, DateTimeOffset.UtcNow, 500m, 0m, 500m, 45m,
            [saleItem]);

        Assert.Single(sale.Items);
    }
}
