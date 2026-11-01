using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Unit.Tests.Entities;

public sealed class PurchaseReceiptValueTests
{
    private static PurchaseReceiptValue Receipt(decimal total = 1000m) =>
        PurchaseReceiptValue.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10m, total, " GRN-1 ", DateTimeOffset.UtcNow, Guid.NewGuid()).Value;

    [Fact]
    public void Create_KeepsTotalAsOriginalAndEffective()
    {
        var receipt = Receipt(100m);

        Assert.Equal(100m, receipt.OriginalTotalCost);
        Assert.Equal(100m, receipt.EffectiveTotalCost);
        Assert.Equal(PurchaseReceiptSource.DirectInbound, receipt.Source);
        Assert.Equal(PurchaseReceiptValueStatus.Resolved, receipt.Status);
        Assert.Equal("GRN-1", receipt.SourceReference);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10.005)]
    [InlineData(100_000_000)]
    public void Create_RejectsInvalidTotal(decimal total)
    {
        var result = PurchaseReceiptValue.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, 1m, total, null, DateTimeOffset.UtcNow, Guid.NewGuid());

        Assert.Equal("PurchaseReceipt.TotalCostInvalid", result.FirstError.Code);
    }

    [Fact]
    public void Create_RejectsNonPositiveQuantity()
    {
        var result = PurchaseReceiptValue.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, 0m, 10m, null, DateTimeOffset.UtcNow, Guid.NewGuid());

        Assert.Equal("PurchaseReceipt.QuantityInvalid", result.FirstError.Code);
    }

    [Fact]
    public void ApplyCostCorrection_ReturnsDeltaFromEffectiveTotalAndKeepsOriginal()
    {
        var receipt = Receipt();

        Assert.Equal(100m, receipt.ApplyCostCorrection(1100m).Value);
        Assert.Equal(-50m, receipt.ApplyCostCorrection(1050m).Value);
        Assert.Equal(1000m, receipt.OriginalTotalCost);
        Assert.Equal(1050m, receipt.EffectiveTotalCost);
    }

    [Fact]
    public void ApplyCostCorrection_WhenUnchanged_IsRefused()
    {
        var receipt = Receipt();

        Assert.Equal("PurchaseReceipt.CostUnchanged", receipt.ApplyCostCorrection(1000m).FirstError.Code);
        Assert.Equal(1000m, receipt.EffectiveTotalCost);
    }

    [Fact]
    public void LinkPurchaseOrderReceiptLine_RecordsSource()
    {
        var receipt = Receipt();
        var lineId = Guid.NewGuid();

        receipt.LinkPurchaseOrderReceiptLine(lineId);

        Assert.Equal(PurchaseReceiptSource.PurchaseOrder, receipt.Source);
        Assert.Equal(lineId, receipt.PurchaseOrderReceiptLineId);
    }

    [Fact]
    public void CostAdjustment_RecordsSignedDelta()
    {
        var adjustment = PurchaseReceiptCostAdjustment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1000m, 1100m, " Invoice ", Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()).Value;

        Assert.Equal(100m, adjustment.Delta);
        Assert.Equal("Invoice", adjustment.Reason);
    }

    [Fact]
    public void CostAdjustment_RequiresReason()
    {
        var result = PurchaseReceiptCostAdjustment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1000m, 1100m, " ", Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid());

        Assert.Equal("PurchaseReceipt.ReasonRequired", result.FirstError.Code);
    }

    [Fact]
    public void CostAdjustedLedgerEntry_RequiresBatch()
    {
        var withoutBatch = SupplierLedgerEntry.Create(Guid.NewGuid(), Guid.NewGuid(), null, SupplierLedgerEntryType.CostAdjusted, 100m, DateOnly.FromDateTime(DateTime.UtcNow), null, Guid.NewGuid());
        var withBatch = SupplierLedgerEntry.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), SupplierLedgerEntryType.CostAdjusted, -50m, DateOnly.FromDateTime(DateTime.UtcNow), null, Guid.NewGuid());

        Assert.Equal("SupplierLedgerEntry.BatchRequired", withoutBatch.FirstError.Code);
        Assert.False(withBatch.IsError);
    }
}
