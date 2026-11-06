using ErrorOr;
using Errors = Intelibill.Application.Common.Errors.Errors;
using Intelibill.Domain.Common;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Services;

/// <param name="SellerProfile">The seller identity in force for this document.</param>
/// <param name="Customer">The resolved customer, when the sale has one; its GSTIN makes the supply B2B.</param>
/// <param name="PlaceOfSupplyStateCode">The state the supply is made in; the seller's state (a counter sale) when null.</param>
/// <param name="IssuedAt">When the document was issued.</param>
/// <param name="FiscalYear">The fiscal year its number belongs to.</param>
/// <param name="DocumentUnits">Each goods item's printed unit (UQC) by item id; an item absent from it prints <c>NOS</c>.</param>
public sealed record InvoiceIssueContext(
    InvoiceSellerProfile SellerProfile,
    Customer? Customer,
    string? PlaceOfSupplyStateCode,
    DateTimeOffset IssuedAt,
    FiscalYear FiscalYear,
    IReadOnlyDictionary<Guid, string>? DocumentUnits = null);

/// <summary>
/// Decides which document a priced sale may be issued as and freezes it. The capability matrix is
/// deliberately narrow: anything it does not recognise is refused rather than issued as a
/// plausible-looking but wrong document. See docs/gst-invoice-capability-matrix.md.
/// </summary>
public static class InvoiceDocumentBuilder
{
    public const string TaxCalculation = "component-split/1";
    public const string GoodsUom = "NOS";
    public const string ServiceUom = "NA";

    /// <summary>Rule 46(e): an unregistered buyer's name, address and state are required at this taxable value.</summary>
    public const decimal UnregisteredBuyerDetailsThreshold = 50_000m;

    /// <summary>The shop's identity and capability as it stands now, or why it cannot issue documents.</summary>
    public static ErrorOr<InvoiceSellerProfile> CaptureSellerProfile(Shop shop)
    {
        if (shop.TaxRegime == TaxRegime.NotDeclared || GstStateCodes.Find(shop.StateCode) is not { } state)
            return Errors.Invoice.TaxProfileRequired;

        string? gstin = null;
        if (shop.TaxRegime is TaxRegime.Regular or TaxRegime.Composition)
        {
            gstin = Gstin.Normalize(shop.GstNumber);
            if (!Gstin.IsValid(gstin) || !string.Equals(Gstin.StateCodeOf(gstin!), state.Code, StringComparison.Ordinal))
                return Errors.Invoice.SellerGstinInvalid;
        }

        if (shop.TaxRegime == TaxRegime.Regular)
        {
            if (shop.EInvoiceStatus == EInvoiceStatus.Required)
                return Errors.Invoice.EInvoiceUnsupported;
            if (shop.EInvoiceStatus == EInvoiceStatus.NotAttested)
                return Errors.Invoice.TaxProfileRequired;
        }

        var address = string.Join(", ", new[] { shop.Address, shop.City, $"{state.Name} {shop.Pincode}".Trim() }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        var seller = new InvoiceSeller(
            shop.LegalName ?? shop.Name,
            shop.Name,
            address,
            state.Code,
            state.Name,
            gstin,
            shop.TaxRegime);

        return new InvoiceSellerProfile(seller, shop.EInvoiceStatus, shop.InvoiceSeriesCode);
    }

    /// <summary>
    /// Builds the document for a priced sale whose number is already allocated, or the reason the
    /// sale cannot be issued. Amounts are taken from the sale as priced, never recomputed.
    /// </summary>
    private static string GoodsUomOf(InvoiceIssueContext context, SaleItem line) =>
        line.ItemId is { } itemId && context.DocumentUnits?.TryGetValue(itemId, out var unit) == true ? unit : GoodsUom;

    public static ErrorOr<IssuedInvoiceSnapshot> Build(InvoiceIssueContext context, Sale sale)
    {
        var profile = context.SellerProfile;
        var seller = profile.Seller;
        if (seller.TaxRegime == TaxRegime.NotDeclared)
            return Errors.Invoice.TaxProfileRequired;
        if (seller.TaxRegime == TaxRegime.Regular && profile.EInvoiceStatus == EInvoiceStatus.Required)
            return Errors.Invoice.EInvoiceUnsupported;

        var lines = sale.Items;
        if (!Reconciles(sale))
            return Errors.Invoice.AmountsDoNotReconcile;

        var buyer = BuildBuyer(context.Customer, sale);
        if (buyer is null)
            return Errors.Invoice.BuyerDetailsRequired;

        var resolvedPlace = ResolvePlaceOfSupply(context.PlaceOfSupplyStateCode, seller, buyer, lines);
        if (resolvedPlace.IsError)
            return resolvedPlace.Errors;

        var placeOfSupply = resolvedPlace.Value;
        var isInterstate = !string.Equals(placeOfSupply.Code, seller.StateCode, StringComparison.Ordinal);
        var collectsTax = lines.Any(line => line.TaxAmount != 0m || line.TaxRatePercent != 0m);

        InvoiceDocumentType documentType;
        var declarations = new List<InvoiceDeclaration>();
        switch (seller.TaxRegime)
        {
            case TaxRegime.Composition:
                if (collectsTax)
                    return Errors.Invoice.CompositionCannotCollectTax;
                if (isInterstate)
                    return Errors.Invoice.CompositionInterstateUnsupported;
                documentType = InvoiceDocumentType.BillOfSupply;
                declarations.Add(InvoiceDeclaration.CompositionTaxablePerson);
                break;

            case TaxRegime.Unregistered:
                if (collectsTax)
                    return Errors.Invoice.UnregisteredCannotCollectTax;
                documentType = InvoiceDocumentType.Receipt;
                declarations.Add(InvoiceDeclaration.NotRegisteredUnderGst);
                break;

            case TaxRegime.Regular:
                var taxableLines = lines.Count(line => line.TaxRatePercent > 0m);
                documentType = taxableLines == lines.Count
                    ? InvoiceDocumentType.TaxInvoice
                    : taxableLines == 0 ? InvoiceDocumentType.BillOfSupply : InvoiceDocumentType.InvoiceCumBillOfSupply;

                // Rule 46A allows one invoice-cum-bill only to an unregistered recipient; a
                // registered one needs a tax invoice and a separate bill of supply.
                if (documentType == InvoiceDocumentType.InvoiceCumBillOfSupply && buyer.IsRegistered)
                    return Errors.Invoice.MixedSupplyToRegisteredBuyer;

                var regularCheck = CheckRegularParticulars(profile.EInvoiceStatus, documentType, buyer, sale);
                if (regularCheck.IsError)
                    return regularCheck.Errors;
                if (profile.EInvoiceStatus == EInvoiceStatus.Exempt)
                    declarations.Add(InvoiceDeclaration.EInvoiceExemption);
                break;

            default:
                return Errors.Invoice.TaxProfileRequired;
        }

        var unionTerritoryTax = placeOfSupply.IsUnionTerritoryWithoutLegislature;
        var documentLines = new List<InvoiceDocumentLine>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var isExempt = seller.TaxRegime == TaxRegime.Regular && line.TaxRatePercent == 0m;
            var components = seller.TaxRegime == TaxRegime.Regular && !isExempt
                ? SplitTax(line.TaxableAmount, line.TaxRatePercent, line.TaxAmount, isInterstate, unionTerritoryTax)
                : [];

            documentLines.Add(new InvoiceDocumentLine(
                i + 1,
                line.LineName,
                line.HsnCode,
                line.LineType == SaleLineType.Goods ? GoodsUomOf(context, line) : ServiceUom,
                line.Quantity,
                line.SalesPrice,
                line.ItemDiscountAmount + line.SaleDiscountAmount,
                line.TaxableAmount,
                line.TaxRatePercent,
                isExempt,
                components,
                line.TotalAmount));
        }

        var totals = new InvoiceDocumentTotals(
            documentLines.Where(l => !l.IsExempt).Sum(l => l.TaxableValue),
            documentLines.Where(l => l.IsExempt).Sum(l => l.TaxableValue),
            documentLines
                .SelectMany(l => l.TaxComponents)
                .GroupBy(c => (c.Kind, c.RatePercent))
                .OrderBy(g => g.Key.Kind)
                .ThenBy(g => g.Key.RatePercent)
                .Select(g => new InvoiceTaxComponent(g.Key.Kind, g.Key.RatePercent, g.Sum(c => c.TaxableBase), g.Sum(c => c.Amount)))
                .ToList(),
            sale.TotalTaxAmount,
            RoundingAdjustment: 0m,
            sale.TotalAmount);

        return new IssuedInvoiceSnapshot(
            IssuedInvoiceSnapshot.CurrentVersion,
            documentType,
            context.IssuedAt,
            BusinessCalendar.DateOf(context.IssuedAt),
            context.FiscalYear.StartYear,
            profile.SeriesCode,
            sale.InvoiceNumber,
            seller,
            buyer,
            placeOfSupply.Code,
            placeOfSupply.Name,
            isInterstate,
            ReverseCharge: false,
            documentLines,
            totals,
            declarations,
            InvoiceSignatureMode.AuthorisedSignatory,
            TaxCalculation);
    }

    /// <summary>
    /// <c>component-split/1</c>: intrastate tax splits into equal-rate central and state (or UT)
    /// halves, the odd paisa going to CGST; interstate tax is IGST at the full rate.
    /// </summary>
    internal static IReadOnlyList<InvoiceTaxComponent> SplitTax(
        decimal taxableValue,
        decimal ratePercent,
        decimal taxAmount,
        bool isInterstate,
        bool unionTerritoryTax)
    {
        if (isInterstate)
            return [new InvoiceTaxComponent(TaxComponentKind.Igst, ratePercent, taxableValue, taxAmount)];

        var halfRate = ratePercent / 2m;
        var central = decimal.Round(taxAmount / 2m, 2, MidpointRounding.AwayFromZero);
        return
        [
            new InvoiceTaxComponent(TaxComponentKind.Cgst, halfRate, taxableValue, central),
            new InvoiceTaxComponent(unionTerritoryTax ? TaxComponentKind.Utgst : TaxComponentKind.Sgst, halfRate, taxableValue, taxAmount - central),
        ];
    }

    /// <summary>
    /// The place of supply when the cashier did not state one. Goods handed over at the counter are
    /// supplied where they are delivered, the seller's state (IGST Act s.10(1)(c)); services go to
    /// the recipient's recorded location, or the seller's when none is on record (s.12(2)). A sale
    /// holding both whose places differ has no single place of supply and must state one.
    /// </summary>
    private static ErrorOr<GstStateCodes.GstState> ResolvePlaceOfSupply(
        string? statedStateCode,
        InvoiceSeller seller,
        InvoiceBuyer buyer,
        IReadOnlyList<SaleItem> lines)
    {
        if (statedStateCode is not null)
            return GstStateCodes.Find(statedStateCode) is { } stated ? stated : Errors.Invoice.PlaceOfSupplyInvalid;

        var hasServices = lines.Any(line => line.LineType == SaleLineType.Service);
        var hasGoods = lines.Any(line => line.LineType != SaleLineType.Service);
        var servicesStateCode = buyer.StateCode ?? seller.StateCode;
        if (hasServices && hasGoods && !string.Equals(servicesStateCode, seller.StateCode, StringComparison.Ordinal))
            return Errors.Invoice.PlaceOfSupplyRequired;

        return GstStateCodes.Find(hasServices ? servicesStateCode : seller.StateCode) is { } place
            ? place
            : Errors.Invoice.PlaceOfSupplyInvalid;
    }

    private static bool Reconciles(Sale sale) =>
        sale.Items.All(line => line.TotalAmount == line.TaxableAmount + line.TaxAmount)
        && sale.Items.Sum(line => line.TaxAmount) == sale.TotalTaxAmount
        && sale.Items.Sum(line => line.TotalAmount) == sale.TotalAmount;

    /// <summary>The buyer as recorded; null when the customer's GSTIN is unusable.</summary>
    private static InvoiceBuyer? BuildBuyer(Customer? customer, Sale sale)
    {
        var gstin = Gstin.Normalize(customer?.Gstin);
        if (gstin is not null && !Gstin.IsValid(gstin))
            return null;

        var state = GstStateCodes.Find(customer?.StateCode ?? (gstin is null ? null : Gstin.StateCodeOf(gstin)));
        return new InvoiceBuyer(
            customer?.Name ?? sale.CustomerName,
            customer?.PhoneNumber ?? sale.CustomerPhone,
            customer?.Address,
            gstin,
            state?.Code,
            state?.Name);
    }

    private static ErrorOr<Success> CheckRegularParticulars(
        EInvoiceStatus eInvoiceStatus,
        InvoiceDocumentType documentType,
        InvoiceBuyer buyer,
        Sale sale)
    {
        var isInvoice = documentType is InvoiceDocumentType.TaxInvoice or InvoiceDocumentType.InvoiceCumBillOfSupply;
        if (buyer.IsRegistered)
        {
            // Rule 46(d): a registered recipient's name, address and GSTIN.
            if (string.IsNullOrWhiteSpace(buyer.Name) || string.IsNullOrWhiteSpace(buyer.Address))
                return Errors.Invoice.BuyerDetailsRequired;
        }
        else if (isInvoice && sale.Items.Sum(line => line.TaxableAmount) >= UnregisteredBuyerDetailsThreshold)
        {
            // Rule 46(e): an unregistered recipient's name, address and state at ₹50,000 or more.
            if (string.IsNullOrWhiteSpace(buyer.Name) || string.IsNullOrWhiteSpace(buyer.Address) || buyer.StateCode is null)
                return Errors.Invoice.BuyerDetailsRequired;
        }

        // HSN digits (Notification 78/2020): turnover above the e-invoice threshold (an exempt
        // registration) needs six digits on every line; at or below it, four on B2B invoices.
        var minimumDigits = eInvoiceStatus == EInvoiceStatus.Exempt ? 6 : buyer.IsRegistered && isInvoice ? 4 : 0;
        if (minimumDigits > 0 && sale.Items.Any(line => !HasHsnDigits(line.HsnCode, minimumDigits)))
            return Errors.Invoice.HsnRequired;

        return Result.Success;
    }

    private static bool HasHsnDigits(string? code, int minimumDigits) =>
        code is { Length: 4 or 6 or 8 } && code.Length >= minimumDigits && code.All(char.IsAsciiDigit);
}
