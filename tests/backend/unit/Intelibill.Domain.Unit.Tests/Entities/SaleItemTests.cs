using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class SaleItemTests
{
    [Fact]
    public void Create_WithValidData_SetsSaleItemProperties()
    {
        var shopId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var batchId = Guid.NewGuid();

        var saleItem = SaleItem.Create(shopId, itemId, batchId,
            quantity: 3m, costPrice: 80m, salesPrice: 100m, mrp: 120m,
            taxRatePercent: 18m, isPriceIncludingTax: false, hasPriceMismatch: false);

        Assert.Equal(shopId, saleItem.ShopId);
        Assert.Equal(itemId, saleItem.ItemId);
        Assert.Equal(batchId, saleItem.InventoryBatchId);
        Assert.Equal(3m, saleItem.Quantity);
        Assert.Equal(80m, saleItem.CostPrice);
        Assert.Equal(100m, saleItem.SalesPrice);
        Assert.Equal(120m, saleItem.Mrp);
        Assert.Equal(18m, saleItem.TaxRatePercent);
        Assert.False(saleItem.IsPriceIncludingTax);
        Assert.False(saleItem.HasPriceMismatch);
    }

    [Fact]
    public void Create_WithPriceMismatch_SetsMismatchFlag()
    {
        var saleItem = SaleItem.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            quantity: 1m, costPrice: 85m, salesPrice: 105m, mrp: 120m,
            taxRatePercent: 5m, isPriceIncludingTax: true, hasPriceMismatch: true);

        Assert.True(saleItem.HasPriceMismatch);
    }
}
