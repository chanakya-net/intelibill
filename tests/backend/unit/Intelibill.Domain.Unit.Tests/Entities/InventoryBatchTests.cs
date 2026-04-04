using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class InventoryBatchTests
{
    [Fact]
    public void GetTaxAmountPerUnit_ComputesFromSalesPriceAndTaxRate()
    {
        var result = InventoryBatch.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "B-01",
            quantity: 100m,
            costPrice: 50m,
            mrp: 80m,
            salesPrice: 70m,
            minSalePrice: 65m,
            taxRatePercent: 18m,
            expiryDate: null,
            manufacturingDate: DateOnly.FromDateTime(DateTime.UtcNow),
            createdBy: Guid.NewGuid());
        Assert.False(result.IsError);

        var batch = result.Value;
        Assert.Equal(12.6m, batch.GetTaxAmountPerUnit());
    }

    [Fact]
    public void Create_TaxRateAboveHundred_ReturnsValidationError()
    {
        var result = InventoryBatch.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "B-01",
            quantity: 100m,
            costPrice: 50m,
            mrp: 80m,
            salesPrice: 70m,
            minSalePrice: 65m,
            taxRatePercent: 101m,
            expiryDate: null,
            manufacturingDate: null,
            createdBy: Guid.NewGuid());

        Assert.True(result.IsError);
        Assert.Equal("InventoryBatch.TaxRateOutOfRange", result.FirstError.Code);
    }

    [Fact]
    public void AddQuantity_WithPositiveQuantity_UpdatesQuantity()
    {
        var createResult = InventoryBatch.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "B-01",
            quantity: 100m,
            costPrice: 50m,
            mrp: 80m,
            salesPrice: 70m,
            minSalePrice: 65m,
            taxRatePercent: 18m,
            expiryDate: null,
            manufacturingDate: null,
            createdBy: Guid.NewGuid());
        Assert.False(createResult.IsError);

        var batch = createResult.Value;
        var updatedBy = Guid.NewGuid();

        var result = batch.AddQuantity(25m, updatedBy);

        Assert.False(result.IsError);
        Assert.Equal(125m, batch.Quantity);
        Assert.Equal(updatedBy, batch.UpdatedBy);
    }
}