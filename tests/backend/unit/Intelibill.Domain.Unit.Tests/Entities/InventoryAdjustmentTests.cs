using ErrorOr;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class InventoryAdjustmentTests
{
    [Fact]
    public void Create_WithValidDecrease_CapturesAdjustmentSnapshot()
    {
        var shopId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var performedAt = new DateTimeOffset(2026, 5, 5, 10, 0, 0, TimeSpan.Zero);
        var performedBy = Guid.NewGuid();
        var createdBy = Guid.NewGuid();

        var result = InventoryAdjustment.Create(
            shopId,
            itemId,
            batchId,
            " ADJ-20260505-0001 ",
            InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Damaged,
            quantity: 2.25m,
            unitCost: 80m,
            costImpact: 180m,
            batchQuantityBefore: 10m,
            batchQuantityAfter: 7.75m,
            inventoryQuantityBefore: 25m,
            inventoryQuantityAfter: 22.75m,
            performedAt,
            performedBy,
            notes: "  Torn packaging  ",
            createdBy);

        Assert.False(result.IsError);
        var adjustment = result.Value;
        Assert.Equal(shopId, adjustment.ShopId);
        Assert.Equal(itemId, adjustment.ItemId);
        Assert.Equal(batchId, adjustment.InventoryBatchId);
        Assert.Equal("ADJ-20260505-0001", adjustment.AdjustmentNumber);
        Assert.Equal(InventoryAdjustmentDirection.Decrease, adjustment.Direction);
        Assert.Equal(InventoryAdjustmentReason.Damaged, adjustment.Reason);
        Assert.Equal(2.25m, adjustment.Quantity);
        Assert.Equal(80m, adjustment.UnitCost);
        Assert.Equal(180m, adjustment.CostImpact);
        Assert.Equal(10m, adjustment.BatchQuantityBefore);
        Assert.Equal(7.75m, adjustment.BatchQuantityAfter);
        Assert.Equal(25m, adjustment.InventoryQuantityBefore);
        Assert.Equal(22.75m, adjustment.InventoryQuantityAfter);
        Assert.Equal(performedAt, adjustment.PerformedAt);
        Assert.Equal(performedBy, adjustment.PerformedBy);
        Assert.Equal("Torn packaging", adjustment.Notes);
        Assert.Equal(createdBy, adjustment.CreatedBy);
        Assert.False(adjustment.IsVoided);
        Assert.Null(adjustment.VoidReason);
        Assert.Null(adjustment.VoidedAt);
        Assert.Null(adjustment.VoidedBy);
        Assert.Null(adjustment.ReversalStockTransactionId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithNonPositiveQuantity_ReturnsValidationError(decimal quantity)
    {
        var result = CreateAdjustment(quantity: quantity);

        Assert.True(result.IsError);
        Assert.Equal("InventoryAdjustment.QuantityMustBePositive", result.FirstError.Code);
    }

    [Fact]
    public void Create_WithMoreThanTwoQuantityDecimals_ReturnsValidationError()
    {
        var result = CreateAdjustment(quantity: 1.125m, batchQuantityAfter: 8.875m, inventoryQuantityAfter: 23.875m);

        Assert.True(result.IsError);
        Assert.Equal("InventoryAdjustment.QuantityScaleInvalid", result.FirstError.Code);
    }

    [Theory]
    [InlineData(InventoryAdjustmentDirection.Decrease, InventoryAdjustmentReason.OtherLoss)]
    [InlineData(InventoryAdjustmentDirection.Increase, InventoryAdjustmentReason.OtherGain)]
    public void Create_WithOtherReasonAndBlankNotes_ReturnsValidationError(
        InventoryAdjustmentDirection direction,
        InventoryAdjustmentReason reason)
    {
        var result = CreateAdjustment(
            direction: direction,
            reason: reason,
            batchQuantityAfter: direction == InventoryAdjustmentDirection.Increase ? 12m : 8m,
            inventoryQuantityAfter: direction == InventoryAdjustmentDirection.Increase ? 27m : 23m,
            notes: " ");

        Assert.True(result.IsError);
        Assert.Equal("InventoryAdjustment.NotesRequired", result.FirstError.Code);
    }

    [Fact]
    public void Create_WithIncreaseDirectionAndLossReason_ReturnsValidationError()
    {
        var result = CreateAdjustment(
            direction: InventoryAdjustmentDirection.Increase,
            reason: InventoryAdjustmentReason.Damaged,
            batchQuantityAfter: 12m,
            inventoryQuantityAfter: 27m);

        Assert.True(result.IsError);
        Assert.Equal("InventoryAdjustment.ReasonDirectionMismatch", result.FirstError.Code);
    }

    [Fact]
    public void Create_WithZeroCostImpact_ReturnsValidationError()
    {
        var result = InventoryAdjustment.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ADJ-20260505-0001",
            InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Damaged,
            quantity: 2m,
            unitCost: 0m,
            costImpact: 0m,
            batchQuantityBefore: 10m,
            batchQuantityAfter: 8m,
            inventoryQuantityBefore: 25m,
            inventoryQuantityAfter: 23m,
            new DateTimeOffset(2026, 5, 5, 10, 0, 0, TimeSpan.Zero),
            Guid.NewGuid(),
            notes: null,
            createdBy: Guid.NewGuid());

        Assert.True(result.IsError);
        Assert.Equal("InventoryAdjustment.CostImpactMustBePositive", result.FirstError.Code);
    }

    [Fact]
    public void Void_WithReasonAndReversalStockTransaction_MarksAdjustmentVoided()
    {
        var adjustment = CreateAdjustment().Value;
        var voidedAt = new DateTimeOffset(2026, 5, 5, 11, 0, 0, TimeSpan.Zero);
        var voidedBy = Guid.NewGuid();
        var reversalStockTransactionId = Guid.NewGuid();

        var result = adjustment.Void(voidedAt, voidedBy, "  Mistaken count  ", reversalStockTransactionId);

        Assert.False(result.IsError);
        Assert.True(adjustment.IsVoided);
        Assert.Equal(voidedAt, adjustment.VoidedAt);
        Assert.Equal(voidedBy, adjustment.VoidedBy);
        Assert.Equal("Mistaken count", adjustment.VoidReason);
        Assert.Equal(reversalStockTransactionId, adjustment.ReversalStockTransactionId);
        Assert.Equal(voidedBy, adjustment.UpdatedBy);
    }

    [Fact]
    public void Void_WithBlankReason_ReturnsValidationError()
    {
        var adjustment = CreateAdjustment().Value;

        var result = adjustment.Void(DateTimeOffset.UtcNow, Guid.NewGuid(), " ", Guid.NewGuid());

        Assert.True(result.IsError);
        Assert.Equal("InventoryAdjustment.VoidReasonRequired", result.FirstError.Code);
    }

    [Fact]
    public void Void_WhenAlreadyVoided_ReturnsValidationError()
    {
        var adjustment = CreateAdjustment().Value;
        var firstVoid = adjustment.Void(DateTimeOffset.UtcNow, Guid.NewGuid(), "Duplicate", Guid.NewGuid());
        Assert.False(firstVoid.IsError);

        var result = adjustment.Void(DateTimeOffset.UtcNow, Guid.NewGuid(), "Duplicate", Guid.NewGuid());

        Assert.True(result.IsError);
        Assert.Equal("InventoryAdjustment.AlreadyVoided", result.FirstError.Code);
    }

    private static ErrorOr<InventoryAdjustment> CreateAdjustment(
        InventoryAdjustmentDirection direction = InventoryAdjustmentDirection.Decrease,
        InventoryAdjustmentReason reason = InventoryAdjustmentReason.Damaged,
        decimal quantity = 2m,
        decimal batchQuantityAfter = 8m,
        decimal inventoryQuantityAfter = 23m,
        string? notes = null) =>
        InventoryAdjustment.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ADJ-20260505-0001",
            direction,
            reason,
            quantity,
            unitCost: 80m,
            costImpact: 160m,
            batchQuantityBefore: 10m,
            batchQuantityAfter,
            inventoryQuantityBefore: 25m,
            inventoryQuantityAfter,
            new DateTimeOffset(2026, 5, 5, 10, 0, 0, TimeSpan.Zero),
            Guid.NewGuid(),
            notes,
            Guid.NewGuid());
}
