using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Unit.Tests.Entities;

public sealed class BatchPriceRevisionTests
{
    private static InventoryBatch Batch() =>
        InventoryBatch.Create(Guid.NewGuid(), Guid.NewGuid(), "B-1", 10m, 100m, 150m, 140m, 5m, false, null, null, null, Guid.NewGuid()).Value;

    [Fact]
    public void ReviseRetailTerms_ChangesPricesInPlaceAndLeavesStockAndCost()
    {
        var batch = Batch();
        var previous = BatchPricing.Of(batch);

        var result = batch.ReviseRetailTerms("B-1", 160m, 150m, 12m, true, new DateOnly(2027, 1, 1), null, Guid.NewGuid());

        Assert.False(result.IsError);
        Assert.Equal(10m, batch.Quantity);
        Assert.Equal(100m, batch.CostPrice);
        Assert.Equal(150m, batch.SalesPrice);
        var revision = BatchPriceRevision.Create(batch, previous, " Festival ", DateTimeOffset.UtcNow, Guid.NewGuid()).Value;
        Assert.Equal(140m, revision.PreviousSalesPrice);
        Assert.Equal(150m, revision.NewSalesPrice);
        Assert.Equal(5m, revision.PreviousTaxRatePercent);
        Assert.Equal(12m, revision.NewTaxRatePercent);
        Assert.True(revision.NewTaxIncluded);
        Assert.Equal("Festival", revision.Reason);
    }

    [Fact]
    public void ReviseRetailTerms_RejectsSaleAboveMrp()
    {
        var batch = Batch();

        var result = batch.ReviseRetailTerms("B-1", 100m, 120m, 5m, false, null, null, Guid.NewGuid());

        Assert.Equal("InventoryBatch.SaleAboveMrp", result.FirstError.Code);
        Assert.Equal(140m, batch.SalesPrice);
    }

    [Fact]
    public void ReviseRetailTerms_RejectsVoidedBatch()
    {
        var batch = Batch();
        batch.Void(Guid.NewGuid());

        Assert.Equal("InventoryBatch.Voided", batch.ReviseRetailTerms("B-1", 150m, 140m, 5m, false, null, null, Guid.NewGuid()).FirstError.Code);
    }

    [Fact]
    public void Create_WhenNoPriceChanged_IsRefused()
    {
        var batch = Batch();

        var result = BatchPriceRevision.Create(batch, BatchPricing.Of(batch), null, DateTimeOffset.UtcNow, Guid.NewGuid());

        Assert.Equal("BatchPriceRevision.Unchanged", result.FirstError.Code);
    }
}
