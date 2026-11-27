using Intelibill.Application.Features.PurchaseOrders.Commands.ReceivePurchaseOrder;

namespace Intelibill.Application.Unit.Tests.Features.PurchaseOrders.Commands.ReceivePurchaseOrder;

public class ReceivePurchaseOrderCommandValidatorTests
{
    private static ReceivePurchaseOrderLineInput Lot() => new(Guid.NewGuid(), "PRODUCT", "LOT-A", 1, 10, 20, 15, 5, false, true, null, null);
    [Fact]
    public void SamePurchaseOrderLine_WithDistinctLotsAndRepeatedBarcode_IsValid()
    {
        var lot = Lot();
        var command = new ReceivePurchaseOrderCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, null,
            [lot, lot with { BatchNumber = "LOT-B" }]);
        Assert.True(new ReceivePurchaseOrderCommandValidator().Validate(command).IsValid);
    }
    [Fact]
    public void ExplicitEmptyClientIdentity_IsRejected()
    {
        var lot = Lot() with { ClientLineId = Guid.Empty };
        Assert.False(new ReceivePurchaseOrderCommandValidator().Validate(new ReceivePurchaseOrderCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, null, [lot])).IsValid);
    }
    [Fact]
    public void DuplicateExplicitClientIdentity_IsRejected()
    {
        var id = Guid.NewGuid();
        var lot = Lot() with { ClientLineId = id };
        var result = new ReceivePurchaseOrderCommandValidator().Validate(new ReceivePurchaseOrderCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, null,
            [lot, lot with { PurchaseOrderLineId = Guid.NewGuid(), BatchNumber = "LOT-B" }]));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Lines[0].ClientLineId");
        Assert.Contains(result.Errors, e => e.PropertyName == "Lines[1].ClientLineId");
    }
    [Theory]
    [InlineData("quantity")]
    [InlineData("cost")]
    [InlineData("mrp")]
    [InlineData("price")]
    [InlineData("over-mrp")]
    [InlineData("negative-tax")]
    [InlineData("over-tax")]
    [InlineData("barcode")]
    [InlineData("long-barcode")]
    [InlineData("batch")]
    [InlineData("long-batch")]
    public void InvalidScalar_IsRejected(string field)
    {
        var lot = Lot();
        lot = field switch
        {
            "quantity" => lot with { Quantity = 0 },
            "cost" => lot with { TotalPurchaseCost = -1 },
            "mrp" => lot with { Mrp = -1 },
            "price" => lot with { SalesPrice = -1 },
            "over-mrp" => lot with { SalesPrice = 21 },
            "negative-tax" => lot with { TaxRatePercent = -1 },
            "over-tax" => lot with { TaxRatePercent = 101 },
            "barcode" => lot with { Barcode = " " },
            "long-barcode" => lot with { Barcode = new string('x', 121) },
            "batch" => lot with { BatchNumber = " " },
            _ => lot with { BatchNumber = new string('x', 81) },
        };
        Assert.False(new ReceivePurchaseOrderCommandValidator().Validate(new ReceivePurchaseOrderCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, null, [lot])).IsValid);
    }
}
