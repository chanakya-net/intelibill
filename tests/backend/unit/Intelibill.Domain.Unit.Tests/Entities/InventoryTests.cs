using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class InventoryTests
{
    [Fact]
    public void UpdateLevels_UpdatesQuantitiesAndUser()
    {
        var createdBy = Guid.NewGuid();
        var updatedBy = Guid.NewGuid();

        var createResult = Inventory.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            quantity: 10m,
            reorderLevel: 4m,
            maxLevel: 20m,
            createdBy);
        Assert.False(createResult.IsError);

        var inventory = createResult.Value;

        var updateResult = inventory.UpdateLevels(quantity: 12.5m, reorderLevel: 5m, maxLevel: 25m, updatedBy);
        Assert.False(updateResult.IsError);

        Assert.Equal(12.5m, inventory.Quantity);
        Assert.Equal(5m, inventory.ReorderLevel);
        Assert.Equal(25m, inventory.MaxLevel);
        Assert.Equal(updatedBy, inventory.UpdatedBy);
    }

    [Fact]
    public void Create_ReorderAboveMax_ReturnsValidationError()
    {
        var result = Inventory.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            quantity: 10m,
            reorderLevel: 21m,
            maxLevel: 20m,
            Guid.NewGuid());

        Assert.True(result.IsError);
        Assert.Equal("Inventory.ReorderExceedsMax", result.FirstError.Code);
    }
}