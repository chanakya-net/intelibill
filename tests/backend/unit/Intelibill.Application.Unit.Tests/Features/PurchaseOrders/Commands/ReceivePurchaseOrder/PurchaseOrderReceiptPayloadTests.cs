using Intelibill.Application.Common.Operations;
using Intelibill.Application.Features.PurchaseOrders.Commands.ReceivePurchaseOrder;

namespace Intelibill.Application.Unit.Tests.Features.PurchaseOrders.Commands.ReceivePurchaseOrder;

public class PurchaseOrderReceiptPayloadTests
{
    private static ReceivePurchaseOrderCommand Command() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "REF", "Note", null,
        [new(Guid.NewGuid(), "PRODUCT", "LOT", 1, 10, 20, 15, 5, false, true, null, null, Guid.NewGuid())]);

    private static string Hash(ReceivePurchaseOrderCommand command) => FinancialOperationHasher.Compute(PurchaseOrderReceiptPayload.From(command));

    [Fact]
    public void EquivalentTrimmedTextDecimalScaleAndUtcInstant_HashAlike()
    {
        var command = Command() with { ReceivedAt = new DateTimeOffset(2026, 6, 7, 10, 0, 0, TimeSpan.Zero) };
        var equivalent = command with { ReferenceNumber = " REF ", Notes = " Note ", ReceivedAt = command.ReceivedAt.Value.ToOffset(TimeSpan.FromHours(5.5)),
            Lines = [command.Lines[0] with { Barcode = " PRODUCT ", BatchNumber = " LOT ", TotalPurchaseCost = 10.00m, Mrp = 20.00m, SalesPrice = 15.00m, TaxRatePercent = 5.00m }] };
        Assert.Equal(Hash(command), Hash(equivalent));
    }

    [Fact]
    public void OmittedReceivedAt_IsStableAndTransportIdentityIsExcluded()
    {
        var command = Command();
        Assert.Equal(Hash(command), Hash(command with { ActorUserId = Guid.NewGuid(), ActiveShopId = Guid.NewGuid(), OperationId = Guid.NewGuid() }));
        Assert.NotEqual(Hash(command), Hash(command with { ReceivedAt = DateTimeOffset.UtcNow }));
    }

    [Fact]
    public void EveryBusinessFieldAndRowOrder_ChangesHash()
    {
        var command = Command();
        var line = command.Lines[0];
        ReceivePurchaseOrderLineInput[] changes = [line with { PurchaseOrderLineId = Guid.NewGuid() }, line with { ClientLineId = null },
            line with { Barcode = "OTHER" }, line with { BatchNumber = "OTHER" }, line with { Quantity = 2 }, line with { TotalPurchaseCost = 11 },
            line with { Mrp = 21 }, line with { SalesPrice = 16 }, line with { TaxRatePercent = 6 }, line with { TaxIncluded = true },
            line with { PurchaseTaxIncluded = false }, line with { ExpiryDate = new DateOnly(2027, 1, 1) }, line with { ManufacturingDate = new DateOnly(2026, 1, 1) }];
        var hash = Hash(command);
        Assert.All(changes, changed => Assert.NotEqual(hash, Hash(command with { Lines = [changed] })));
        Assert.NotEqual(hash, Hash(command with { PurchaseOrderId = Guid.NewGuid() }));
        Assert.NotEqual(hash, Hash(command with { ReferenceNumber = "OTHER" }));
        Assert.NotEqual(hash, Hash(command with { Notes = "OTHER" }));
        Assert.NotEqual(Hash(command with { Lines = [line, changes[0]] }), Hash(command with { Lines = [changes[0], line] }));
    }
}
