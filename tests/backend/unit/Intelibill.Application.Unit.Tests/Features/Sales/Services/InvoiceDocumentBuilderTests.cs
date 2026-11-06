using System.Text.Json;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Services;

public class InvoiceDocumentBuilderTests
{
    private const string KarnatakaSeller = "29AAPFU0939F1ZR";
    private const string ChandigarhSeller = "04AAPFU0939F1Z3";
    private const string KarnatakaBuyer = "29AABCS1234C1ZT";
    private const string TamilNaduBuyer = "33AABCT1332L1ZL";
    private static readonly DateTimeOffset IssuedAt = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public void regime_selects_document_type()
    {
        Assert.Equal(InvoiceDocumentType.TaxInvoice, Build(RegularShop(), [Taxable(100m, 18m)]).Value.DocumentType);
        Assert.Equal(InvoiceDocumentType.BillOfSupply, Build(RegularShop(), [Untaxed(50m)]).Value.DocumentType);
        Assert.Equal(InvoiceDocumentType.InvoiceCumBillOfSupply, Build(RegularShop(), [Taxable(100m, 18m), Untaxed(50m)]).Value.DocumentType);

        var composition = Build(Shop(TaxRegime.Composition, "29", KarnatakaSeller), [Untaxed(80m)]).Value;
        Assert.Equal(InvoiceDocumentType.BillOfSupply, composition.DocumentType);
        Assert.Contains(InvoiceDeclaration.CompositionTaxablePerson, composition.Declarations);
        Assert.Empty(composition.Totals.Components);
        Assert.Equal(KarnatakaSeller, composition.Seller.Gstin);

        var receipt = Build(Shop(TaxRegime.Unregistered, "29", null), [Untaxed(80m)]).Value;
        Assert.Equal(InvoiceDocumentType.Receipt, receipt.DocumentType);
        Assert.Null(receipt.Seller.Gstin);
        Assert.Contains(InvoiceDeclaration.NotRegisteredUnderGst, receipt.Declarations);
        Assert.Equal(0m, receipt.Totals.TotalTax);
    }

    [Fact]
    public void exempt_lines_carry_no_components_and_count_as_exempt_value()
    {
        var document = Build(RegularShop(), [Taxable(100m, 18m), Untaxed(50m)]).Value;

        Assert.Equal(100m, document.Totals.TaxableValue);
        Assert.Equal(50m, document.Totals.ExemptValue);
        Assert.True(document.Lines[1].IsExempt);
        Assert.Empty(document.Lines[1].TaxComponents);
    }

    [Fact]
    public void composition_and_unregistered_sellers_cannot_collect_tax()
    {
        Assert.Equal("Invoice.CompositionCannotCollectTax", Build(Shop(TaxRegime.Composition, "29", KarnatakaSeller), [Taxable(100m, 18m)]).FirstError.Code);
        Assert.Equal("Invoice.UnregisteredCannotCollectTax", Build(Shop(TaxRegime.Unregistered, "29", null), [Taxable(100m, 5m)]).FirstError.Code);
    }

    [Fact]
    public void unsupported_transaction_is_blocked()
    {
        var undeclared = global::Intelibill.Domain.Entities.Shop.Create("Shop", "1 Road", "City", "Karnataka", "560001", null, null, KarnatakaSeller);
        Assert.Equal("Invoice.TaxProfileRequired", InvoiceDocumentBuilder.CaptureSellerProfile(undeclared).FirstError.Code);

        var eInvoice = Shop(TaxRegime.Regular, "29", KarnatakaSeller, EInvoiceStatus.Required);
        Assert.Equal("Invoice.EInvoiceUnsupported", InvoiceDocumentBuilder.CaptureSellerProfile(eInvoice).FirstError.Code);

        var composition = Shop(TaxRegime.Composition, "29", KarnatakaSeller);
        Assert.Equal("Invoice.CompositionInterstateUnsupported", Build(composition, [Untaxed(80m)], placeOfSupply: "33").FirstError.Code);

        Assert.Equal("Invoice.PlaceOfSupplyInvalid", Build(RegularShop(), [Taxable(100m, 18m)], placeOfSupply: "96").FirstError.Code);
        Assert.Equal("Invoice.PlaceOfSupplyInvalid", Build(RegularShop(), [Taxable(100m, 18m)], placeOfSupply: "97").FirstError.Code);
    }

    [Fact]
    public void seller_gstin_changes_only_through_the_tax_profile()
    {
        var shop = RegularShop();

        var changed = shop.UpdateDetails("Shop", "1 Road", "City", "Karnataka", "560001", null, null, "27AAPFU0939F1ZV");
        Assert.Equal("Shop.GstinManagedByTaxProfile", changed.FirstError.Code);
        Assert.Equal(KarnatakaSeller, shop.GstNumber);

        Assert.False(shop.UpdateDetails("Shop", "1 Road", "City", "Karnataka", "560001", null, null, null).IsError);
        Assert.Equal(KarnatakaSeller, shop.GstNumber);
        Assert.Equal(KarnatakaSeller, InvoiceDocumentBuilder.CaptureSellerProfile(shop).Value.Seller.Gstin);
    }

    [Fact]
    public void component_tax_reconciles()
    {
        var intrastate = Build(RegularShop(), [Taxable(100m, 18m)]).Value;
        AssertComponents(intrastate, (TaxComponentKind.Cgst, 9m, 9m), (TaxComponentKind.Sgst, 9m, 9m));
        Assert.False(intrastate.IsInterstate);
        Assert.Equal(118m, intrastate.Totals.InvoiceTotal);
        Assert.Equal(0m, intrastate.Totals.RoundingAdjustment);

        var interstate = Build(RegularShop(), [Taxable(100m, 18m)], placeOfSupply: "33").Value;
        AssertComponents(interstate, (TaxComponentKind.Igst, 18m, 18m));
        Assert.True(interstate.IsInterstate);
        Assert.Equal("Tamil Nadu", interstate.PlaceOfSupplyStateName);
        Assert.Equal(118m, interstate.Totals.InvoiceTotal);

        var unionTerritory = Build(Shop(TaxRegime.Regular, "04", ChandigarhSeller), [Taxable(100m, 18m)]).Value;
        AssertComponents(unionTerritory, (TaxComponentKind.Cgst, 9m, 9m), (TaxComponentKind.Utgst, 9m, 9m));

        // An odd-paisa tax splits with the extra paisa on CGST and still sums to the line tax.
        var oddPaisa = Build(RegularShop(), [Line(0.28m, 18m, 0.05m)]).Value;
        AssertComponents(oddPaisa, (TaxComponentKind.Cgst, 9m, 0.03m), (TaxComponentKind.Sgst, 9m, 0.02m));
        Assert.Equal(oddPaisa.Totals.TotalTax, oddPaisa.Totals.Components.Sum(c => c.Amount));
        Assert.Equal(oddPaisa.Totals.InvoiceTotal, oddPaisa.Totals.TaxableValue + oddPaisa.Totals.TotalTax);
    }

    [Fact]
    public void components_group_by_kind_and_rate_across_lines()
    {
        var document = Build(RegularShop(), [Taxable(100m, 18m), Taxable(200m, 18m), Taxable(100m, 5m)]).Value;

        Assert.Equal(
            [(TaxComponentKind.Cgst, 2.5m, 100m, 2.5m), (TaxComponentKind.Cgst, 9m, 300m, 27m), (TaxComponentKind.Sgst, 2.5m, 100m, 2.5m), (TaxComponentKind.Sgst, 9m, 300m, 27m)],
            document.Totals.Components.Select(c => (c.Kind, c.RatePercent, c.TaxableBase, c.Amount)));
    }

    [Fact]
    public void amounts_that_do_not_reconcile_are_refused()
    {
        Assert.Equal("Invoice.AmountsDoNotReconcile", Build(RegularShop(), [Line(100m, 18m, 18m, total: 117m)], paidOverride: 117m).FirstError.Code);
    }

    [Fact]
    public void registered_buyer_needs_name_and_address_and_hsn()
    {
        var withoutAddress = Customer.Create(Guid.NewGuid(), "Buyer Pvt Ltd", "9876543210", null, gstin: KarnatakaBuyer);
        Assert.Equal("Invoice.BuyerDetailsRequired", Build(RegularShop(), [Taxable(100m, 18m, hsn: "8471")], customer: withoutAddress).FirstError.Code);

        var buyer = Customer.Create(Guid.NewGuid(), "Buyer Pvt Ltd", "9876543210", "4 Market St, Bengaluru", gstin: KarnatakaBuyer);
        Assert.Equal("Invoice.HsnRequired", Build(RegularShop(), [Taxable(100m, 18m, hsn: null)], customer: buyer).FirstError.Code);
        Assert.Equal("Invoice.HsnRequired", Build(RegularShop(), [Taxable(100m, 18m, hsn: "84A1")], customer: buyer).FirstError.Code);

        var document = Build(RegularShop(), [Taxable(100m, 18m, hsn: "8471")], customer: buyer).Value;
        Assert.Equal(KarnatakaBuyer, document.Buyer.Gstin);
        Assert.Equal("29", document.Buyer.StateCode);
        Assert.Equal("Karnataka", document.Buyer.StateName);
    }

    [Fact]
    public void registered_buyer_in_another_state_is_still_intrastate_for_a_counter_sale()
    {
        var buyer = Customer.Create(Guid.NewGuid(), "TN Traders", "9876543210", "2 Anna Salai, Chennai", gstin: TamilNaduBuyer);

        var document = Build(RegularShop(), [Taxable(100m, 18m, hsn: "8471")], customer: buyer).Value;

        Assert.False(document.IsInterstate);
        Assert.Equal("29", document.PlaceOfSupplyStateCode);
        AssertComponents(document, (TaxComponentKind.Cgst, 9m, 9m), (TaxComponentKind.Sgst, 9m, 9m));
    }

    [Fact]
    public void services_are_supplied_where_the_recipient_is()
    {
        var registered = Customer.Create(Guid.NewGuid(), "TN Traders", "9876543210", "2 Anna Salai, Chennai", gstin: TamilNaduBuyer);
        var toRegistered = Build(RegularShop(), [Service(100m, 18m)], customer: registered).Value;
        Assert.True(toRegistered.IsInterstate);
        Assert.Equal("33", toRegistered.PlaceOfSupplyStateCode);
        AssertComponents(toRegistered, (TaxComponentKind.Igst, 18m, 18m));

        // An unregistered recipient's recorded address decides it too; without one the seller's state does.
        var recorded = Customer.Create(Guid.NewGuid(), "Walk In", "9876543210", "9 Lake Rd, Chennai", stateCode: "33");
        Assert.True(Build(RegularShop(), [Service(100m, 18m)], customer: recorded).Value.IsInterstate);
        Assert.False(Build(RegularShop(), [Service(100m, 18m)]).Value.IsInterstate);
    }

    [Fact]
    public void goods_and_services_with_different_places_of_supply_need_a_stated_place()
    {
        var buyer = Customer.Create(Guid.NewGuid(), "TN Traders", "9876543210", "2 Anna Salai, Chennai", gstin: TamilNaduBuyer);

        Assert.Equal("Invoice.PlaceOfSupplyRequired", Build(RegularShop(), [Taxable(100m, 18m), Service(50m, 18m)], customer: buyer).FirstError.Code);

        var stated = Build(RegularShop(), [Taxable(100m, 18m), Service(50m, 18m)], placeOfSupply: "33", customer: buyer).Value;
        Assert.True(stated.IsInterstate);
        AssertComponents(stated, (TaxComponentKind.Igst, 18m, 27m));
    }

    [Fact]
    public void taxable_and_exempt_items_for_a_registered_buyer_are_refused()
    {
        var buyer = Customer.Create(Guid.NewGuid(), "Buyer Pvt Ltd", "9876543210", "4 Market St, Bengaluru", gstin: KarnatakaBuyer);

        Assert.Equal("Invoice.MixedSupplyToRegisteredBuyer", Build(RegularShop(), [Taxable(100m, 18m), Untaxed(50m)], customer: buyer).FirstError.Code);
        Assert.Equal(InvoiceDocumentType.TaxInvoice, Build(RegularShop(), [Taxable(100m, 18m)], customer: buyer).Value.DocumentType);
        Assert.Equal(InvoiceDocumentType.BillOfSupply, Build(RegularShop(), [Untaxed(50m)], customer: buyer).Value.DocumentType);
    }

    [Fact]
    public void large_unregistered_sale_needs_buyer_details()
    {
        Assert.Equal("Invoice.BuyerDetailsRequired", Build(RegularShop(), [Taxable(50_000m, 18m)]).FirstError.Code);

        var buyer = Customer.Create(Guid.NewGuid(), "Walk In", "9876543210", "9 Lake Rd", stateCode: "29");
        Assert.False(Build(RegularShop(), [Taxable(50_000m, 18m)], customer: buyer).IsError);
        Assert.False(Build(RegularShop(), [Taxable(49_999.99m, 18m)]).IsError);
    }

    [Fact]
    public void exempt_registration_needs_six_digit_hsn_and_carries_its_declaration()
    {
        var shop = Shop(TaxRegime.Regular, "29", KarnatakaSeller, EInvoiceStatus.Exempt);

        Assert.Equal("Invoice.HsnRequired", Build(shop, [Taxable(100m, 18m, hsn: "8471")]).FirstError.Code);

        var document = Build(shop, [Taxable(100m, 18m, hsn: "847130")]).Value;
        Assert.Contains(InvoiceDeclaration.EInvoiceExemption, document.Declarations);
    }

    [Fact]
    public void document_records_number_series_dates_and_uom()
    {
        var document = Build(RegularShop(), [Taxable(100m, 18m), Service(50m, 18m)]).Value;

        Assert.Equal(IssuedInvoiceSnapshot.CurrentVersion, document.Version);
        Assert.Equal("A/2627/000001", document.Number);
        Assert.Equal("A", document.SeriesCode);
        Assert.Equal(2026, document.FiscalYearStart);
        Assert.Equal(new DateOnly(2026, 11, 3), document.BusinessDate);
        Assert.Equal(["NOS", "NA"], document.Lines.Select(l => l.Uom));
        Assert.Equal(InvoiceSignatureMode.AuthorisedSignatory, document.SignatureMode);
        Assert.False(document.ReverseCharge);
        Assert.Equal("component-split/1", document.TaxCalculation);
        Assert.Equal("Main Shop Pvt Ltd", document.Seller.LegalName);
        Assert.Equal("Main Shop", document.Seller.TradeName);
        Assert.Equal("42 MG Road, Bengaluru, Karnataka 560001", document.Seller.Address);
    }

    [Fact]
    public void snapshot_survives_master_edits()
    {
        var shop = RegularShop();
        var customer = Customer.Create(shop.Id, "Buyer Pvt Ltd", "9876543210", "4 Market St", gstin: KarnatakaBuyer);
        var document = Build(shop, [Taxable(100m, 18m, hsn: "8471")], customer: customer).Value;
        var before = JsonSerializer.Serialize(document);

        shop.UpdateDetails("Renamed Shop", "1 New Rd", "Mysuru", "Karnataka", "570001", null, null, KarnatakaSeller);
        Assert.False(shop.DeclareTaxProfile("New Legal Name", "29", TaxRegime.Regular, KarnatakaSeller, "B", EInvoiceStatus.BelowThreshold, Actor, IssuedAt).IsError);
        customer.Update("Other Buyer", "9999999999", "Elsewhere", true, 0m, TamilNaduBuyer, null);

        Assert.Equal(before, JsonSerializer.Serialize(document));
        Assert.Equal("Main Shop Pvt Ltd", document.Seller.LegalName);
        Assert.Equal(KarnatakaBuyer, document.Buyer.Gstin);
    }

    private static void AssertComponents(IssuedInvoiceSnapshot document, params (TaxComponentKind Kind, decimal Rate, decimal Amount)[] expected) =>
        Assert.Equal(expected, document.Totals.Components.Select(c => (c.Kind, c.RatePercent, c.Amount)));

    private static Shop RegularShop() => Shop(TaxRegime.Regular, "29", KarnatakaSeller);

    private static Shop Shop(TaxRegime regime, string stateCode, string? gstin, EInvoiceStatus eInvoice = EInvoiceStatus.BelowThreshold)
    {
        var shop = global::Intelibill.Domain.Entities.Shop.Create("Main Shop", "42 MG Road", "Bengaluru", "Karnataka", "560001", null, null, gstin);
        var declared = shop.DeclareTaxProfile("Main Shop Pvt Ltd", stateCode, regime, gstin, null, eInvoice, Actor, IssuedAt);
        Assert.False(declared.IsError, declared.IsError ? declared.FirstError.Code : null);
        return shop;
    }

    private static SaleLineInput Taxable(decimal taxable, decimal rate, string? hsn = "8471")
    {
        var tax = decimal.Round(taxable * rate / 100m, 2, MidpointRounding.AwayFromZero);
        return Line(taxable, rate, tax, hsn: hsn);
    }

    private static SaleLineInput Untaxed(decimal value) => Line(value, 0m, 0m);

    private static SaleLineInput Line(decimal taxable, decimal rate, decimal tax, decimal? total = null, string? hsn = "8471") =>
        new(Guid.Empty, SaleLineType.Goods, Guid.NewGuid(), Guid.NewGuid(), null, "Item", "CODE", 1m, 1m, taxable, taxable, rate, false, false,
            PreTaxAmountBeforeDiscount: taxable, TaxableAmount: taxable, TaxAmount: tax, TotalAmount: total ?? taxable + tax, HsnCode: hsn);

    private static SaleLineInput Service(decimal taxable, decimal rate)
    {
        var tax = decimal.Round(taxable * rate / 100m, 2, MidpointRounding.AwayFromZero);
        return new(Guid.Empty, SaleLineType.Service, null, null, Guid.NewGuid(), "Repair", "SVC", 1m, 0m, taxable, 0m, rate, false, false,
            PreTaxAmountBeforeDiscount: taxable, TaxableAmount: taxable, TaxAmount: tax, TotalAmount: taxable + tax, HsnCode: "998719");
    }

    private static ErrorOr.ErrorOr<IssuedInvoiceSnapshot> Build(
        Shop shop,
        IReadOnlyList<SaleLineInput> lines,
        string? placeOfSupply = null,
        Customer? customer = null,
        decimal? paidOverride = null)
    {
        var total = paidOverride ?? lines.Sum(l => l.TotalAmount!.Value);
        var sale = Sale.Record(shop.Id, Actor, "key", "hash", "A/2627/000001", lines, customer?.Id, customer?.Name, customer?.PhoneNumber, PaymentMethod.Cash, total, 0m, IssuedAt, global::Intelibill.Domain.ValueObjects.BusinessDay.InDefaultZone(IssuedAt));
        Assert.False(sale.IsError, sale.IsError ? sale.FirstError.Code : null);

        var profile = InvoiceDocumentBuilder.CaptureSellerProfile(shop);
        Assert.False(profile.IsError, profile.IsError ? profile.FirstError.Code : null);

        return InvoiceDocumentBuilder.Build(
            new InvoiceIssueContext(profile.Value, customer, placeOfSupply, IssuedAt, FiscalYear.ForDate(IssuedAt)),
            sale.Value);
    }
}
