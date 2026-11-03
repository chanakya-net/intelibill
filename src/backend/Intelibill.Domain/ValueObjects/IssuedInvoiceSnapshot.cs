using Intelibill.Domain.Enums;

namespace Intelibill.Domain.ValueObjects;

/// <summary>
/// The document a sale was issued as, frozen at issue: who sold to whom, where, which lines at which
/// amounts and which tax components. Reprints, exports and audits read this, never the live shop or
/// customer, so later master-data edits cannot change an issued copy.
/// </summary>
/// <remarks>Version 1 is the implicit legacy document (no snapshot); this shape is version 2.</remarks>
public sealed record IssuedInvoiceSnapshot(
    int Version,
    InvoiceDocumentType DocumentType,
    DateTimeOffset IssuedAt,
    DateOnly BusinessDate,
    int FiscalYearStart,
    string SeriesCode,
    string Number,
    InvoiceSeller Seller,
    InvoiceBuyer Buyer,
    string PlaceOfSupplyStateCode,
    string PlaceOfSupplyStateName,
    bool IsInterstate,
    bool ReverseCharge,
    IReadOnlyList<InvoiceDocumentLine> Lines,
    InvoiceDocumentTotals Totals,
    IReadOnlyList<InvoiceDeclaration> Declarations,
    InvoiceSignatureMode SignatureMode,
    string TaxCalculation)
{
    public const int CurrentVersion = 2;
}

public sealed record InvoiceSeller(
    string LegalName,
    string TradeName,
    string Address,
    string StateCode,
    string StateName,
    string? Gstin,
    TaxRegime TaxRegime);

public sealed record InvoiceBuyer(
    string? Name,
    string? Phone,
    string? Address,
    string? Gstin,
    string? StateCode,
    string? StateName)
{
    public bool IsRegistered => Gstin is not null;
}

public sealed record InvoiceDocumentLine(
    int LineNumber,
    string Description,
    string? HsnOrSac,
    string Uom,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal TaxableValue,
    decimal TaxRatePercent,
    bool IsExempt,
    IReadOnlyList<InvoiceTaxComponent> TaxComponents,
    decimal LineTotal);

public sealed record InvoiceTaxComponent(
    TaxComponentKind Kind,
    decimal RatePercent,
    decimal TaxableBase,
    decimal Amount);

public sealed record InvoiceDocumentTotals(
    decimal TaxableValue,
    decimal ExemptValue,
    IReadOnlyList<InvoiceTaxComponent> Components,
    decimal TotalTax,
    decimal RoundingAdjustment,
    decimal InvoiceTotal);

/// <summary>Statutory wording a document must carry; the print renders each in the reader's language.</summary>
public enum InvoiceDeclaration
{
    /// <summary>"Composition taxable person, not eligible to collect tax on supplies."</summary>
    CompositionTaxablePerson = 1,

    /// <summary>The Rule 46 declaration of a registration exempt from e-invoicing despite its turnover.</summary>
    EInvoiceExemption = 2,

    /// <summary>The seller is not registered under GST.</summary>
    NotRegisteredUnderGst = 3,
}

public enum InvoiceSignatureMode
{
    /// <summary>A printed copy carries an authorised-signatory line for the supplier.</summary>
    AuthorisedSignatory = 1,
}

/// <summary>
/// The seller identity and capability a shop had when it issued (or, offline, when it reserved the
/// range the device issues from).
/// </summary>
public sealed record InvoiceSellerProfile(
    InvoiceSeller Seller,
    EInvoiceStatus EInvoiceStatus,
    string SeriesCode);
