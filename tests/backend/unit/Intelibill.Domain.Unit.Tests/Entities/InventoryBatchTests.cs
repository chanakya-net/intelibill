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
            taxRatePercent: 18m,
            taxIncluded: false,
            expiryDate: null,
            manufacturingDate: DateOnly.FromDateTime(DateTime.UtcNow),
            supplierId: null,
            createdBy: Guid.NewGuid());
        Assert.False(result.IsError);

        var batch = result.Value;
        Assert.Equal(12.6m, batch.GetTaxAmountPerUnit());
    }

    [Fact]
    public void GetProfitCostPrice_WhenPurchaseTaxIncluded_ReturnsNetUnitCost()
    {
        var result = InventoryBatch.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "B-01",
            quantity: 10m,
            costPrice: 118m,
            mrp: 160m,
            salesPrice: 140m,
            taxRatePercent: 18m,
            taxIncluded: false,
            expiryDate: null,
            manufacturingDate: null,
            supplierId: null,
            createdBy: Guid.NewGuid(),
            purchaseTaxIncluded: true);
        Assert.False(result.IsError);

        Assert.Equal(100m, result.Value.GetProfitCostPrice());
    }

    [Fact]
    public void GetProfitCostPrice_WhenPurchaseTaxExcluded_ReturnsStoredUnitCost()
    {
        var result = InventoryBatch.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "B-01",
            quantity: 10m,
            costPrice: 118m,
            mrp: 160m,
            salesPrice: 140m,
            taxRatePercent: 18m,
            taxIncluded: false,
            expiryDate: null,
            manufacturingDate: null,
            supplierId: null,
            createdBy: Guid.NewGuid());
        Assert.False(result.IsError);

        Assert.Equal(118m, result.Value.GetProfitCostPrice());
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
            taxRatePercent: 101m,
            taxIncluded: false,
            expiryDate: null,
            manufacturingDate: null,
            supplierId: null,
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
            taxRatePercent: 18m,
            taxIncluded: false,
            expiryDate: null,
            manufacturingDate: null,
            supplierId: null,
            createdBy: Guid.NewGuid());
        Assert.False(createResult.IsError);

        var batch = createResult.Value;
        var updatedBy = Guid.NewGuid();

        var result = batch.AddQuantity(25m, updatedBy);

        Assert.False(result.IsError);
        Assert.Equal(125m, batch.Quantity);
        Assert.Equal(updatedBy, batch.UpdatedBy);
    }

    [Fact]
    public void SubtractQuantity_WithValidQuantity_DecreasesQuantity()
    {
        var createResult = InventoryBatch.Create(
            Guid.NewGuid(), Guid.NewGuid(), "B-01",
            quantity: 100m, costPrice: 50m, mrp: 80m, salesPrice: 70m,
            taxRatePercent: 18m, taxIncluded: false, expiryDate: null,
            manufacturingDate: null, supplierId: null, createdBy: Guid.NewGuid());
        var batch = createResult.Value;
        var updatedBy = Guid.NewGuid();

        var result = batch.SubtractQuantity(30m, updatedBy);

        Assert.False(result.IsError);
        Assert.Equal(70m, batch.Quantity);
        Assert.Equal(updatedBy, batch.UpdatedBy);
    }

    [Fact]
    public void SubtractQuantity_WithZeroQuantity_ReturnsValidationError()
    {
        var createResult = InventoryBatch.Create(
            Guid.NewGuid(), Guid.NewGuid(), "B-01",
            quantity: 100m, costPrice: 50m, mrp: 80m, salesPrice: 70m,
            taxRatePercent: 18m, taxIncluded: false, expiryDate: null,
            manufacturingDate: null, supplierId: null, createdBy: Guid.NewGuid());
        var batch = createResult.Value;

        var result = batch.SubtractQuantity(0m, Guid.NewGuid());

        Assert.True(result.IsError);
        Assert.Equal("InventoryBatch.QuantitySubtractionInvalid", result.FirstError.Code);
    }

    [Fact]
    public void SubtractQuantity_ExceedingAvailable_ReturnsValidationError()
    {
        var createResult = InventoryBatch.Create(
            Guid.NewGuid(), Guid.NewGuid(), "B-01",
            quantity: 10m, costPrice: 50m, mrp: 80m, salesPrice: 70m,
            taxRatePercent: 18m, taxIncluded: false, expiryDate: null,
            manufacturingDate: null, supplierId: null, createdBy: Guid.NewGuid());
        var batch = createResult.Value;

        var result = batch.SubtractQuantity(11m, Guid.NewGuid());

        Assert.True(result.IsError);
        Assert.Equal("InventoryBatch.InsufficientStock", result.FirstError.Code);
    }

    [Fact]
    public void StockValueContribution_IsQuantityTimesGetProfitCostPrice()
    {
        var batch = InventoryBatch.Create(
            Guid.NewGuid(), Guid.NewGuid(), "B-01",
            quantity: 10m, costPrice: 118m, mrp: 160m, salesPrice: 140m,
            taxRatePercent: 18m, taxIncluded: false, expiryDate: null,
            manufacturingDate: null, supplierId: null, createdBy: Guid.NewGuid(),
            purchaseTaxIncluded: true).Value;

        var stockValue = batch.Quantity * batch.GetProfitCostPrice();

        Assert.Equal(1000m, stockValue);
    }

    [Fact]
    public void StockValueContribution_VoidedBatch_ShouldBeZeroQuantity()
    {
        var batch = InventoryBatch.Create(
            Guid.NewGuid(), Guid.NewGuid(), "B-02",
            quantity: 10m, costPrice: 50m, mrp: 80m, salesPrice: 70m,
            taxRatePercent: 0m, taxIncluded: false, expiryDate: null,
            manufacturingDate: null, supplierId: null, createdBy: Guid.NewGuid()).Value;

        batch.Void(Guid.NewGuid());

        Assert.Equal(0m, batch.Quantity);
        Assert.True(batch.IsVoided);
        Assert.Equal(0m, batch.Quantity * batch.GetProfitCostPrice());
    }
}
