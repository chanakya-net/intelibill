using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

public sealed class PurchaseOrder : BaseEntity
{
    private readonly List<PurchaseOrderLine> _lines = [];

    public Guid ShopId { get; private set; }
    public string PurchaseOrderNumber { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public string? SupplierName { get; private set; }
    public string? SupplierReference { get; private set; }
    public PurchaseOrderStatus Status { get; private set; }
    public IReadOnlyList<PurchaseOrderLine> Lines => _lines.AsReadOnly();

    public decimal ExpectedTotal => _lines.Sum(l => l.LineTotal);

    private PurchaseOrder() { }

    public static PurchaseOrder CreateDraft(
        Guid shopId,
        string purchaseOrderNumber,
        string? notes,
        string? supplierName = null,
        string? supplierReference = null)
    {
        return new PurchaseOrder
        {
            ShopId = shopId,
            PurchaseOrderNumber = purchaseOrderNumber,
            Notes = NormalizeOptional(notes),
            SupplierName = NormalizeOptional(supplierName),
            SupplierReference = NormalizeOptional(supplierReference),
            Status = PurchaseOrderStatus.Draft,
        };
    }

    public PurchaseOrderLine AddLine(
        string description,
        int expectedQuantity,
        decimal unitCost,
        int receivedQuantity = 0)
    {
        var line = PurchaseOrderLine.Create(Id, description, expectedQuantity, unitCost, receivedQuantity);
        _lines.Add(line);
        return line;
    }

    public void UpdateSupplierDetails(string? supplierName, string? supplierReference)
    {
        SupplierName = NormalizeOptional(supplierName);
        SupplierReference = NormalizeOptional(supplierReference);
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Trim();
    }
}
