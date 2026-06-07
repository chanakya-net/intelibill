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
    public string? SupplierName { get; private set; }
    public string? SupplierReference { get; private set; }
    public PurchaseOrderStatus Status { get; private set; }
    public string? CancellationReason { get; private set; }
    public IReadOnlyList<PurchaseOrderLine> Lines => _lines.AsReadOnly();

    public decimal ExpectedTotal => _lines.Sum(l => l.LineTotal);

    public bool CanDeleteDraft => Status == PurchaseOrderStatus.Draft;

    private PurchaseOrder() { }

    public static PurchaseOrder CreateDraft(
        Guid shopId,
        string purchaseOrderNumber,
        Guid? supplierId,
        DateOnly? orderDate,
        DateOnly? expectedDeliveryDate,
        string? supplierReferenceNumber,
        string? notes,
        string? supplierName = null,
        string? supplierReference = null)
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
            SupplierName = NormalizeOptional(supplierName),
            SupplierReference = NormalizeOptional(supplierReference),
            Status = PurchaseOrderStatus.Draft,
        };
    }

    public PurchaseOrderLine AddLine(
        Guid itemId,
        string description,
        int expectedQuantity,
        decimal unitCost,
        int receivedQuantity = 0)
    {
        var line = PurchaseOrderLine.Create(Id, itemId, description, expectedQuantity, unitCost, receivedQuantity);
        _lines.Add(line);
        return line;
    }

    public void UpdateSupplierDetails(string? supplierName, string? supplierReference)
    {
        SupplierName = NormalizeOptional(supplierName);
        SupplierReference = NormalizeOptional(supplierReference);
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

    public void Place(Guid supplierId)
    {
        if (Status != PurchaseOrderStatus.Draft)
            throw new InvalidOperationException("Only draft purchase orders can be placed.");

        if (_lines.Count == 0)
            throw new InvalidOperationException("Cannot place a purchase order with no lines.");

        SupplierId = supplierId;
        Status = PurchaseOrderStatus.Placed;
    }

    public void Cancel(string reason)
    {
        if (Status != PurchaseOrderStatus.Placed)
            throw new InvalidOperationException("Only placed purchase orders can be cancelled.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Cancellation reason is required.");

        var totalReceived = _lines.Sum(l => l.ReceivedQuantity);
        if (totalReceived > 0)
            throw new InvalidOperationException("Cannot cancel a purchase order that has received items.");

        CancellationReason = reason.Trim();
        Status = PurchaseOrderStatus.Cancelled;
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Trim();
    }
}
