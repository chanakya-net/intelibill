using ErrorOr;
using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

public sealed class Inventory : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid ItemId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal ReorderLevel { get; private set; }
    public decimal MaxLevel { get; private set; }
    public DateTimeOffset LastUpdatedAt { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public Item Item { get; private set; } = null!;

    private Inventory() { }

    public static ErrorOr<Inventory> Create(
        Guid shopId,
        Guid itemId,
        decimal quantity,
        decimal reorderLevel,
        decimal maxLevel,
        Guid createdBy)
    {
        var levelValidation = ValidateLevels(quantity, reorderLevel, maxLevel);
        if (levelValidation.IsError)
        {
            return levelValidation.Errors;
        }

        return new Inventory
        {
            ShopId = shopId,
            ItemId = itemId,
            Quantity = quantity,
            ReorderLevel = reorderLevel,
            MaxLevel = maxLevel,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            CreatedBy = createdBy,
        };
    }

    public ErrorOr<Success> UpdateLevels(decimal quantity, decimal reorderLevel, decimal maxLevel, Guid updatedBy)
    {
        var levelValidation = ValidateLevels(quantity, reorderLevel, maxLevel);
        if (levelValidation.IsError)
        {
            return levelValidation.Errors;
        }

        Quantity = quantity;
        ReorderLevel = reorderLevel;
        MaxLevel = maxLevel;
        LastUpdatedAt = DateTimeOffset.UtcNow;
        UpdatedBy = updatedBy;

        return Result.Success;
    }

    public ErrorOr<Success> AddQuantity(decimal quantityToAdd, Guid updatedBy)
    {
        if (quantityToAdd <= 0)
        {
            return Error.Validation("Inventory.QuantityAdditionInvalid", "Quantity to add must be greater than zero.");
        }

        Quantity += quantityToAdd;
        LastUpdatedAt = DateTimeOffset.UtcNow;
        UpdatedBy = updatedBy;

        return Result.Success;
    }

    public ErrorOr<Success> SubtractQuantity(decimal quantityToSubtract, Guid updatedBy)
    {
        if (quantityToSubtract < 0)
        {
            return Error.Validation("Inventory.QuantitySubtractionInvalid", "Quantity to subtract cannot be negative.");
        }

        if (Quantity - quantityToSubtract < 0)
        {
            return Error.Conflict("Inventory.InsufficientStock", "Not enough stock available for this operation.");
        }

        Quantity -= quantityToSubtract;
        LastUpdatedAt = DateTimeOffset.UtcNow;
        UpdatedBy = updatedBy;

        return Result.Success;
    }

    public void MarkUpdatedBy(Guid updatedBy)
    {
        UpdatedBy = updatedBy;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }

    private static ErrorOr<Success> ValidateLevels(decimal quantity, decimal reorderLevel, decimal maxLevel)
    {
        if (quantity < 0)
        {
            return Error.Validation("Inventory.QuantityNegative", "Quantity cannot be negative.");
        }

        if (reorderLevel < 0)
        {
            return Error.Validation("Inventory.ReorderLevelNegative", "Reorder level cannot be negative.");
        }

        if (maxLevel < 0)
        {
            return Error.Validation("Inventory.MaxLevelNegative", "Max level cannot be negative.");
        }

        if (reorderLevel > maxLevel)
        {
            return Error.Validation("Inventory.ReorderExceedsMax", "Reorder level cannot exceed max level.");
        }

        return Result.Success;
    }
}