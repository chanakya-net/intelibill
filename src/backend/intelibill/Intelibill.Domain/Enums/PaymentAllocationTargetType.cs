namespace Intelibill.Domain.Enums;

/// <summary>What an allocation settles. Stored as text; never renumber.</summary>
public enum PaymentAllocationTargetType
{
    /// <summary>A customer's credit sale (<c>sales.id</c>); reduces the invoice's current outstanding.</summary>
    CustomerInvoice = 1,

    /// <summary>A supplier's purchase receipt liability (<c>purchase_receipt_values.id</c>).</summary>
    SupplierReceipt = 2,
}
