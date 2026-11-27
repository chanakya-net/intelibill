using ErrorOr;

namespace Intelibill.Domain.Common;

public static partial class Errors
{
    public static class TaxProfile
    {
        public static Error TaxRegimeRequired =>
            Error.Validation("Shop.TaxRegimeRequired", "Choose the shop's GST registration: regular, composition or not registered.");

        public static Error StateCodeInvalid =>
            Error.Validation("Shop.StateCodeInvalid", "Choose the state or union territory the shop is registered in.");

        public static Error GstinRequiredForRegime =>
            Error.Validation("Shop.GstinRequiredForRegime", "A GST-registered shop needs its GSTIN.");

        public static Error GstinInvalid =>
            Error.Validation("Shop.GstinInvalid", "The GSTIN is not valid: check its characters and check digit.");

        public static Error GstinStateMismatch =>
            Error.Validation("Shop.GstinStateMismatch", "The GSTIN belongs to a different state than the one chosen.");

        public static Error GstinNotAllowedForUnregistered =>
            Error.Validation("Shop.GstinNotAllowedForUnregistered", "A shop that is not registered under GST cannot have a GSTIN.");

        public static Error InvoiceSeriesCodeInvalid =>
            Error.Validation("Shop.InvoiceSeriesCodeInvalid", "The invoice series code must be 1 to 4 letters or digits.");

        public static Error LegalNameTooLong =>
            Error.Validation("Shop.LegalNameTooLong", "The legal name must be 200 characters or fewer.");

        public static Error GstinManagedByTaxProfile =>
            Error.Validation("Shop.GstinManagedByTaxProfile", "The GSTIN is part of the shop's tax profile; change it there.");

        public static Error EInvoiceAttestationRequired =>
            Error.Validation("Shop.EInvoiceAttestationRequired", "A regular GST shop must state whether e-invoicing applies to it.");
    }
}
