using System.Reflection;
using Intelibill.Domain.Common;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class SaleTests
{
    [Fact]
    public void Record_WithValidData_ComputesTotalsAndCreatesItems()
    {
        var shopId = Guid.NewGuid();
        var invoiceNumber = "INV-20260416-ABCD1234";
        var customerId = Guid.NewGuid();
        var soldAt = DateTimeOffset.UtcNow;
        SaleLineInput[] lines =
        [
            new SaleLineInput(
                shopId,
                SaleLineType.Goods,
                ItemId: Guid.NewGuid(),
                InventoryBatchId: Guid.NewGuid(),
                ServiceId: null,
                LineName: "Sugar",
                LineCode: "BC-001",
                Quantity: 2m,
                CostPrice: 80m,
                SalesPrice: 100m,
                Mrp: 120m,
                TaxRatePercent: 18m,
                IsPriceIncludingTax: false,
                HasPriceMismatch: false),
            new SaleLineInput(
                shopId,
                SaleLineType.Goods,
                ItemId: Guid.NewGuid(),
                InventoryBatchId: Guid.NewGuid(),
                ServiceId: null,
                LineName: "Tea",
                LineCode: "BC-002",
                Quantity: 1m,
                CostPrice: 70m,
                SalesPrice: 118m,
                Mrp: 120m,
                TaxRatePercent: 18m,
                IsPriceIncludingTax: true,
                HasPriceMismatch: true),
        ];

        var result = Sale.Record(
            shopId,
            Guid.NewGuid(),
            $"sale-{Guid.NewGuid():N}",
            "HASH-TEST",
            invoiceNumber,
            lines,
            customerId,
            "  Ravi Kumar  ",
            "  +919876543210  ",
            PaymentMethod.Cash,
            354m,
            0m,
            soldAt);

        Assert.False(result.IsError);
        var sale = result.Value;

        Assert.Equal(shopId, sale.ShopId);
        Assert.Equal(invoiceNumber, sale.InvoiceNumber);
        Assert.Equal(customerId, sale.CustomerId);
        Assert.Equal("Ravi Kumar", sale.CustomerName);
        Assert.Equal("+919876543210", sale.CustomerPhone);
        Assert.Equal(PaymentMethod.Cash, sale.PaymentMethod);
        Assert.Equal(soldAt, sale.SoldAt);
        Assert.Equal(300m, sale.SubtotalBeforeDiscount);
        Assert.Equal(354m, sale.TotalBeforeDiscount);
        Assert.Equal(0m, sale.TotalDiscountAmount);
        Assert.Equal(354m, sale.TotalAmount);
        Assert.Equal(54m, sale.TotalTaxAmount);
        Assert.Equal(InstantDiscountType.None, sale.SaleDiscountOverrideType);
        Assert.Equal(0m, sale.SaleDiscountOverrideValue);
        Assert.Equal(2, sale.Items.Count);
        Assert.Equal(200m, sale.Items[0].SalesPrice * sale.Items[0].Quantity);
        Assert.Equal(100m, sale.Items[0].OriginalSalesPrice);
        Assert.Equal(100m, sale.Items[0].FinalSalesPrice);
        Assert.Equal(200m, sale.Items[0].PreTaxAmountBeforeDiscount);
        Assert.Equal(0m, sale.Items[0].ItemDiscountAmount);
        Assert.Equal(0m, sale.Items[0].SaleDiscountAmount);
        Assert.Equal(200m, sale.Items[0].TaxableAmount);
        Assert.Equal(36m, sale.Items[0].TaxAmount);
        Assert.Equal(236m, sale.Items[0].TotalAmount);
        Assert.False(sale.Items[0].IsPriceIncludingTax);
        Assert.Equal(100m, sale.Items[1].PreTaxAmountBeforeDiscount);
        Assert.Equal(18m, sale.Items[1].TaxAmount);
        Assert.Equal(118m, sale.Items[1].TotalAmount);
        Assert.True(sale.Items[1].IsPriceIncludingTax);
        Assert.True(sale.Items[1].HasPriceMismatch);
    }

    [Fact]
    public void Record_WithServiceLine_CreatesServiceSaleItem()
    {
        var shopId = Guid.NewGuid();
        var soldAt = DateTimeOffset.UtcNow;
        var serviceId = Guid.NewGuid();

        var line = new SaleLineInput(
            shopId,
            SaleLineType.Service,
            ItemId: null,
            InventoryBatchId: null,
            ServiceId: serviceId,
            LineName: "Consultation",
            LineCode: "SRV-001",
            Quantity: 1m,
            CostPrice: 0m,
            SalesPrice: 100m,
            Mrp: 0m,
            TaxRatePercent: 0m,
            IsPriceIncludingTax: false,
            HasPriceMismatch: false);

        var result = Sale.Record(
            shopId,
            Guid.NewGuid(),
            $"sale-{Guid.NewGuid():N}",
            "HASH-TEST",
            "INV-20260416-ABCD1234",
            [line],
            null,
            null,
            null,
            PaymentMethod.Cash,
            100m,
            0m,
            soldAt);

        Assert.False(result.IsError);
        var sale = result.Value;
        Assert.Single(sale.Items);
        Assert.Equal(SaleLineType.Service, sale.Items[0].LineType);
        Assert.Equal(serviceId, sale.Items[0].ServiceId);
        Assert.Null(sale.Items[0].ItemId);
        Assert.Null(sale.Items[0].InventoryBatchId);
        Assert.Equal("Consultation", sale.Items[0].LineName);
        Assert.Equal("SRV-001", sale.Items[0].LineCode);
    }

    [Fact]
    public void Record_WhenServiceLineHasGoodsRefs_ReturnsInvalidLineError()
    {
        var shopId = Guid.NewGuid();
        var line = new SaleLineInput(
            shopId,
            SaleLineType.Service,
            ItemId: Guid.NewGuid(),
            InventoryBatchId: Guid.NewGuid(),
            ServiceId: Guid.NewGuid(),
            LineName: "Consultation",
            LineCode: "SRV-001",
            Quantity: 1m,
            CostPrice: 0m,
            SalesPrice: 100m,
            Mrp: 0m,
            TaxRatePercent: 0m,
            IsPriceIncludingTax: false,
            HasPriceMismatch: false);

        var result = Sale.Record(
            shopId,
            Guid.NewGuid(),
            $"sale-{Guid.NewGuid():N}",
            "HASH-TEST",
            "INV-20260416-ABCD1234",
            [line],
            null,
            null,
            null,
            PaymentMethod.Cash,
            100m,
            0m,
            DateTimeOffset.UtcNow);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.InvalidLineReferences.Code, result.FirstError.Code);
    }

    [Fact]
    public void Record_WhenPaidAndDueDoNotMatchRoundedTotal_ReturnsMismatchError()
    {
        var shopId = Guid.NewGuid();
        var line = new SaleLineInput(
            shopId,
            SaleLineType.Goods,
            ItemId: Guid.NewGuid(),
            InventoryBatchId: Guid.NewGuid(),
            ServiceId: null,
            LineName: "Sugar",
            LineCode: "BC-001",
            Quantity: 2m,
            CostPrice: 10m,
            SalesPrice: 50.0025m,
            Mrp: 60m,
            TaxRatePercent: 0m,
            IsPriceIncludingTax: false,
            HasPriceMismatch: false);

        var result = Sale.Record(
            shopId,
            Guid.NewGuid(),
            $"sale-{Guid.NewGuid():N}",
            "HASH-TEST",
            "INV-20260416-ABCD1234",
            [line],
            null,
            null,
            null,
            PaymentMethod.Cash,
            100.004m,
            0m,
            DateTimeOffset.UtcNow);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.PaidAndDueAmountMismatch.Code, result.FirstError.Code);
    }

    [Fact]
    public void Record_WhenCreditHasNoDue_ReturnsCreditRequiresDueAmount()
    {
        var shopId = Guid.NewGuid();
        var line = new SaleLineInput(
            shopId,
            SaleLineType.Goods,
            ItemId: Guid.NewGuid(),
            InventoryBatchId: Guid.NewGuid(),
            ServiceId: null,
            LineName: "Sugar",
            LineCode: "BC-001",
            Quantity: 1m,
            CostPrice: 80m,
            SalesPrice: 100m,
            Mrp: 120m,
            TaxRatePercent: 18m,
            IsPriceIncludingTax: false,
            HasPriceMismatch: false);

        var result = Sale.Record(
            shopId,
            Guid.NewGuid(),
            $"sale-{Guid.NewGuid():N}",
            "HASH-TEST",
            "INV-20260416-ABCD1234",
            [line],
            null,
            null,
            null,
            PaymentMethod.Credit,
            118m,
            0m,
            DateTimeOffset.UtcNow);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.CreditRequiresDueAmount.Code, result.FirstError.Code);
    }

    [Fact]
    public void Record_WhenNoLines_ReturnsItemsRequired()
    {
        var result = Sale.Record(
            Guid.NewGuid(),
            Guid.NewGuid(),
            $"sale-{Guid.NewGuid():N}",
            "HASH-TEST",
            "INV-20260416-ABCD1234",
            [],
            null,
            null,
            null,
            PaymentMethod.Cash,
            0m,
            0m,
            DateTimeOffset.UtcNow);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.ItemsRequired.Code, result.FirstError.Code);
    }

    [Fact]
    public void Record_WithCreditNoteApplied_TotalSplitValidatesWhenSumMatches()
    {
        var shopId = Guid.NewGuid();
        SaleLineInput[] lines =
        [
            new SaleLineInput(
                shopId, SaleLineType.Goods, ItemId: Guid.NewGuid(), InventoryBatchId: Guid.NewGuid(),
                ServiceId: null, LineName: "Sugar", LineCode: "BC-001", Quantity: 2m, CostPrice: 80m,
                SalesPrice: 100m, Mrp: 120m, TaxRatePercent: 18m, IsPriceIncludingTax: false, HasPriceMismatch: false),
        ];

        // total = 2*100*(1+0.18) = 236; paid=136, due=0, creditNoteApplied=100 => 136+0+100=236
        var result = Sale.Record(
            shopId, Guid.NewGuid(), $"sale-{Guid.NewGuid():N}", "HASH-CREDIT",
            "INV-20260614-CRED0001", lines, null, null, null,
            PaymentMethod.Cash, 136m, 0m, DateTimeOffset.UtcNow,
            creditNoteAppliedAmount: 100m);

        Assert.False(result.IsError);
        Assert.Equal(100m, result.Value.CreditNoteAppliedAmount);
    }

    [Fact]
    public void Record_WithCreditNoteApplied_WhenSumMismatches_ReturnsMismatchError()
    {
        var shopId = Guid.NewGuid();
        SaleLineInput[] lines =
        [
            new SaleLineInput(
                shopId, SaleLineType.Goods, ItemId: Guid.NewGuid(), InventoryBatchId: Guid.NewGuid(),
                ServiceId: null, LineName: "Sugar", LineCode: "BC-001", Quantity: 2m, CostPrice: 80m,
                SalesPrice: 100m, Mrp: 120m, TaxRatePercent: 18m, IsPriceIncludingTax: false, HasPriceMismatch: false),
        ];

        // total = 236; paid=136, due=0, creditNoteApplied=50 => 136+0+50=186 != 236
        var result = Sale.Record(
            shopId, Guid.NewGuid(), $"sale-{Guid.NewGuid():N}", "HASH-CREDIT-BAD",
            "INV-20260614-CRED0002", lines, null, null, null,
            PaymentMethod.Cash, 136m, 0m, DateTimeOffset.UtcNow,
            creditNoteAppliedAmount: 50m);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.PaidAndDueAmountMismatch.Code, result.FirstError.Code);
    }

    [Fact]
    public void Create_IsNotPublic()
    {
        var method = typeof(Sale).GetMethod("Create", BindingFlags.Public | BindingFlags.Static);

        Assert.Null(method);
    }

    [Fact]
    public void Record_IsPublic()
    {
        var method = typeof(Sale).GetMethod(nameof(Sale.Record), BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);
    }
}
