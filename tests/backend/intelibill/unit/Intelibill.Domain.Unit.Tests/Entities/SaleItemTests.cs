using System.Reflection;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class SaleItemTests
{
    [Fact]
    public void Create_WithValidData_SetsSaleItemProperties()
    {
        var shopId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var batchId = Guid.NewGuid();

        var saleItem = SaleItem.CreateGoods(shopId, itemId, batchId,
            lineName: "Sugar",
            lineCode: "BC-001",
            quantity: 3m, costPrice: 80m, salesPrice: 100m, mrp: 120m,
            taxRatePercent: 18m, isPriceIncludingTax: false, hasPriceMismatch: false);

        Assert.Equal(shopId, saleItem.ShopId);
        Assert.Equal(SaleLineType.Goods, saleItem.LineType);
        Assert.Equal(itemId, saleItem.ItemId);
        Assert.Equal(batchId, saleItem.InventoryBatchId);
        Assert.Null(saleItem.ServiceId);
        Assert.Equal("Sugar", saleItem.LineName);
        Assert.Equal("BC-001", saleItem.LineCode);
        Assert.Equal(3m, saleItem.Quantity);
        Assert.Equal(80m, saleItem.CostPrice);
        Assert.Equal(100m, saleItem.SalesPrice);
        Assert.Equal(120m, saleItem.Mrp);
        Assert.Equal(18m, saleItem.TaxRatePercent);
        Assert.False(saleItem.IsPriceIncludingTax);
        Assert.False(saleItem.HasPriceMismatch);
        Assert.Equal(100m, saleItem.OriginalSalesPrice);
        Assert.Equal(100m, saleItem.FinalSalesPrice);
        Assert.Equal(300m, saleItem.PreTaxAmountBeforeDiscount);
        Assert.Equal(0m, saleItem.ItemDiscountAmount);
        Assert.Equal(0m, saleItem.SaleDiscountAmount);
        Assert.Equal(300m, saleItem.TaxableAmount);
        Assert.Equal(54m, saleItem.TaxAmount);
        Assert.Equal(354m, saleItem.TotalAmount);
        Assert.Equal(InstantDiscountType.None, saleItem.ItemDiscountOverrideType);
        Assert.Equal(0m, saleItem.ItemDiscountOverrideValue);
        Assert.Null(saleItem.HsnCode);
    }

    [Fact]
    public void Create_WithBlankHsnCode_NormalizesToNull()
    {
        var saleItem = SaleItem.CreateGoods(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            lineName: "Sugar",
            lineCode: "BC-001",
            quantity: 1m, costPrice: 80m, salesPrice: 100m, mrp: 120m,
            taxRatePercent: 5m, isPriceIncludingTax: true, hasPriceMismatch: false,
            hsnCode: "   ");

        Assert.Null(saleItem.HsnCode);
    }

    [Fact]
    public void Create_WithHsnCode_SetsNormalizedValue()
    {
        var saleItem = SaleItem.CreateGoods(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            lineName: "Sugar",
            lineCode: "BC-001",
            quantity: 1m, costPrice: 80m, salesPrice: 100m, mrp: 120m,
            taxRatePercent: 5m, isPriceIncludingTax: true, hasPriceMismatch: false,
            hsnCode: " 0902 ");

        Assert.Equal("0902", saleItem.HsnCode);
    }

    [Fact]
    public void Create_WithPriceMismatch_SetsMismatchFlag()
    {
        var saleItem = SaleItem.CreateGoods(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            lineName: "Sugar",
            lineCode: "BC-001",
            quantity: 1m, costPrice: 85m, salesPrice: 105m, mrp: 120m,
            taxRatePercent: 5m, isPriceIncludingTax: true, hasPriceMismatch: true);

        Assert.True(saleItem.HasPriceMismatch);
        Assert.Equal(100m, saleItem.PreTaxAmountBeforeDiscount);
        Assert.Equal(5m, saleItem.TaxAmount);
        Assert.Equal(105m, saleItem.TotalAmount);
    }

    [Fact]
    public void Create_IsNotPublic()
    {
        var method = typeof(SaleItem).GetMethod("CreateGoods", BindingFlags.Public | BindingFlags.Static);

        Assert.Null(method);
    }
}
