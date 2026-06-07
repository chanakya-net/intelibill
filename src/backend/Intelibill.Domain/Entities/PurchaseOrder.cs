using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

public sealed class PurchaseOrder : BaseEntity
{
    private readonly List<PurchaseOrderLine> _lines = [];

    public Guid ShopId { get; private set; }
    public string PurchaseOrderNumber { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public PurchaseOrderStatus Status { get; private set; }
    public IReadOnlyList<PurchaseOrderLine> Lines => _lines.AsReadOnly();

    public decimal ExpectedTotal => _lines.Sum(l => l.LineTotal);

    private PurchaseOrder() { }

    public static PurchaseOrder CreateDraft(
        Guid shopId,
        string purchaseOrderNumber,
        string? notes)
    {
        return new PurchaseOrder
        {
            ShopId = shopId,
            PurchaseOrderNumber = purchaseOrderNumber,
            Notes = NormalizeOptional(notes),
            Status = PurchaseOrderStatus.Draft,
        };
    }

    public PurchaseOrderLine AddLine(string description, int expectedQuantity, decimal unitCost)
    {
        var line = PurchaseOrderLine.Create(Id, description, expectedQuantity, unitCost);
        _lines.Add(line);
        return line;
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Trim();
    }
}
