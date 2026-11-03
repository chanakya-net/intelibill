namespace Intelibill.Domain.Enums;

/// <summary>The kind of document a sale was issued as.</summary>
public enum InvoiceDocumentType
{
    /// <summary>A regular registered seller's supply that is wholly taxable (Rule 46).</summary>
    TaxInvoice = 1,

    /// <summary>A regular seller's supply mixing taxable and exempt lines (Rule 46A).</summary>
    InvoiceCumBillOfSupply = 2,

    /// <summary>Wholly exempt supply, or any supply by a composition seller (section 31(3)(c), Rule 49).</summary>
    BillOfSupply = 3,

    /// <summary>A seller not registered under GST; no tax is collected.</summary>
    Receipt = 4,
}
