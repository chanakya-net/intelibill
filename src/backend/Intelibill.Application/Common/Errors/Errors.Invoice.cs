using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Invoice
    {
        public static Error TaxProfileRequired =>
            Error.Conflict("Invoice.TaxProfileRequired", "Declare the shop's GST registration in shop settings before issuing documents.");

        public static Error SellerGstinInvalid =>
            Error.Conflict("Invoice.SellerGstinInvalid", "The shop's GSTIN is missing, invalid or from another state; correct the shop's tax profile.");

        public static Error EInvoiceUnsupported =>
            Error.Conflict("Invoice.EInvoiceUnsupported", "E-invoicing applies to this registration and is not supported yet, so documents cannot be issued.");

        public static Error CompositionCannotCollectTax =>
            Error.Validation("Invoice.CompositionCannotCollectTax", "A composition seller cannot charge GST; set the items' tax rate to zero.");

        public static Error CompositionInterstateUnsupported =>
            Error.Validation("Invoice.CompositionInterstateUnsupported", "A composition seller cannot make an interstate supply.");

        public static Error UnregisteredCannotCollectTax =>
            Error.Validation("Invoice.UnregisteredCannotCollectTax", "A shop not registered under GST cannot charge GST; set the items' tax rate to zero.");

        public static Error PlaceOfSupplyInvalid =>
            Error.Validation("Invoice.PlaceOfSupplyInvalid", "The place of supply must be an Indian state or union territory code.");

        public static Error PlaceOfSupplyRequired =>
            Error.Validation("Invoice.PlaceOfSupplyRequired", "This sale mixes goods handed over here with services for a buyer in another state. State the place of supply or bill them separately.");

        public static Error MixedSupplyToRegisteredBuyer =>
            Error.Validation("Invoice.MixedSupplyToRegisteredBuyer", "Taxable and exempt items for a GST-registered buyer need a tax invoice and a separate bill of supply. Bill them as two sales.");

        public static Error BuyerDetailsRequired =>
            Error.Validation("Invoice.BuyerDetailsRequired", "This invoice needs the buyer's name, address and state.");

        public static Error HsnRequired =>
            Error.Validation("Invoice.HsnRequired", "Every line on this invoice needs a valid HSN or SAC code.");

        public static Error AmountsDoNotReconcile =>
            Error.Conflict("Invoice.AmountsDoNotReconcile", "The sale's line amounts do not add up to its totals, so no document was issued.");

        public static Error SeriesRegisteredToAnotherShop =>
            Error.Conflict("Invoice.SeriesRegisteredToAnotherShop", "Another shop under this GSTIN already uses this invoice series this year; choose a different series code.");

        public static Error SeriesRegistrationChanged =>
            Error.Conflict("Invoice.SeriesRegistrationChanged", "This series was opened under a different GST registration this year; choose a new series code.");

        public static Error SeriesExhausted =>
            Error.Conflict("Invoice.SeriesExhausted", "The invoice series has no numbers left this year; choose a new series code.");
    }
}
