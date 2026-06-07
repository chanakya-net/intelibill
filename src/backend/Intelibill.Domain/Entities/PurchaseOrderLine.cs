using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

public sealed class PurchaseOrderLine : BaseEntity
{
    public Guid PurchaseOrderId { get; private set; }
    public Guid ItemId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public int ExpectedQuantity { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal LineTotal => ExpectedQuantity * UnitCost;

    private PurchaseOrderLine() { }

    internal static PurchaseOrderLine Create(
        Guid purchaseOrderId,
        Guid itemId,
        string description,
        int expectedQuantity,
        decimal unitCost)
    {
        if (expectedQuantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedQuantity), "Expected quantity must be positive.");

        if (unitCost < 0)
            throw new ArgumentOutOfRangeException(nameof(unitCost), "Unit cost cannot be negative.");

        return new PurchaseOrderLine
        {
            PurchaseOrderId = purchaseOrderId,
            ItemId = itemId,
            Description = description.Trim(),
            ExpectedQuantity = expectedQuantity,
            UnitCost = unitCost,
        };
    }

    internal void Update(Guid itemId, string description, int expectedQuantity, decimal unitCost)
    {
        if (expectedQuantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedQuantity), "Expected quantity must be positive.");

        if (unitCost < 0)
            throw new ArgumentOutOfRangeException(nameof(unitCost), "Unit cost cannot be negative.");

        ItemId = itemId;
        Description = description.Trim();
        ExpectedQuantity = expectedQuantity;
        UnitCost = unitCost;
    }
}
