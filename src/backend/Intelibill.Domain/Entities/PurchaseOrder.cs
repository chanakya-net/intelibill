using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

public sealed class PurchaseOrder : BaseEntity
{
    private readonly List<PurchaseOrderLine> _lines = [];

    public Guid ShopId { get; private set; }
    public Guid? SupplierId { get; private set; }
    public string PurchaseOrderNumber { get; private set; } = string.Empty;
    public DateOnly? OrderDate { get; private set; }
    public DateOnly? ExpectedDeliveryDate { get; private set; }
    public string? SupplierReferenceNumber { get; private set; }
    public string? Notes { get; private set; }
    public PurchaseOrderStatus Status { get; private set; }
    public IReadOnlyList<PurchaseOrderLine> Lines => _lines.AsReadOnly();

    public decimal ExpectedTotal => _lines.Sum(l => l.LineTotal);

    private PurchaseOrder() { }

    public static PurchaseOrder CreateDraft(
        Guid shopId,
        string purchaseOrderNumber,
        Guid? supplierId,
        DateOnly? orderDate,
        DateOnly? expectedDeliveryDate,
        string? supplierReferenceNumber,
        string? notes)
    {
        return new PurchaseOrder
        {
            ShopId = shopId,
            SupplierId = supplierId,
            PurchaseOrderNumber = purchaseOrderNumber,
            OrderDate = orderDate,
            ExpectedDeliveryDate = expectedDeliveryDate,
            SupplierReferenceNumber = NormalizeOptional(supplierReferenceNumber),
            Notes = NormalizeOptional(notes),
            Status = PurchaseOrderStatus.Draft,
        };
    }

    public PurchaseOrderLine AddLine(Guid itemId, string description, int expectedQuantity, decimal unitCost)
    {
        var line = PurchaseOrderLine.Create(Id, itemId, description, expectedQuantity, unitCost);
        _lines.Add(line);
        return line;
    }

    public void UpdateDraft(
        Guid? supplierId,
        DateOnly? orderDate,
        DateOnly? expectedDeliveryDate,
        string? supplierReferenceNumber,
        string? notes,
        IReadOnlyList<(Guid ItemId, string Description, int ExpectedQuantity, decimal UnitCost)> lines)
    {
        if (Status != PurchaseOrderStatus.Draft)
            throw new InvalidOperationException("Only draft purchase orders can be updated.");

        SupplierId = supplierId;
        OrderDate = orderDate;
        ExpectedDeliveryDate = expectedDeliveryDate;
        SupplierReferenceNumber = NormalizeOptional(supplierReferenceNumber);
        Notes = NormalizeOptional(notes);
        _lines.Clear();
        foreach (var line in lines)
        {
            AddLine(line.ItemId, line.Description, line.ExpectedQuantity, line.UnitCost);
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Trim();
    }
}
