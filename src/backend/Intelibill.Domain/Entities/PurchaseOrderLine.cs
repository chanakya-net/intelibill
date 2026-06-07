using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

public sealed class PurchaseOrderLine : BaseEntity
{
    public Guid PurchaseOrderId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public int ExpectedQuantity { get; private set; }
    public int ReceivedQuantity { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal LineTotal => ExpectedQuantity * UnitCost;

    private PurchaseOrderLine() { }

    internal static PurchaseOrderLine Create(
        Guid purchaseOrderId,
        string description,
        int expectedQuantity,
        decimal unitCost,
        int receivedQuantity = 0)
    {
        ValidateQuantities(expectedQuantity, receivedQuantity);
        ValidateUnitCost(unitCost);

        return new PurchaseOrderLine
        {
            PurchaseOrderId = purchaseOrderId,
            Description = description.Trim(),
            ExpectedQuantity = expectedQuantity,
            ReceivedQuantity = receivedQuantity,
            UnitCost = unitCost,
        };
    }

    internal void Update(string description, int expectedQuantity, decimal unitCost)
    {
        ValidateQuantities(expectedQuantity, ReceivedQuantity);
        ValidateUnitCost(unitCost);

        Description = description.Trim();
        ExpectedQuantity = expectedQuantity;
        UnitCost = unitCost;
    }

    public void RecordReceivedQuantity(int receivedQuantity)
    {
        ValidateQuantities(ExpectedQuantity, receivedQuantity);
        ReceivedQuantity = receivedQuantity;
    }

    private static void ValidateQuantities(int expectedQuantity, int receivedQuantity)
    {
        if (expectedQuantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedQuantity), "Expected quantity must be positive.");

        if (receivedQuantity < 0)
            throw new ArgumentOutOfRangeException(nameof(receivedQuantity), "Received quantity cannot be negative.");

        if (receivedQuantity > expectedQuantity)
            throw new ArgumentOutOfRangeException(nameof(receivedQuantity), "Received quantity cannot exceed expected quantity.");
    }

    private static void ValidateUnitCost(decimal unitCost)
    {
        if (unitCost < 0)
            throw new ArgumentOutOfRangeException(nameof(unitCost), "Unit cost cannot be negative.");
    }
}
