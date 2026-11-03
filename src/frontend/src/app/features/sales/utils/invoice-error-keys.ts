// The GST document refusals the server answers when it cannot issue an invoice (online sale, invoice lease) mapped
// to user messages. Anything not listed keeps the caller's own fallback.

const INVOICE_ERROR_KEYS: Readonly<Record<string, string>> = {
  'Invoice.TaxProfileRequired': 'errors.invoice.taxProfileRequired',
  'Invoice.EInvoiceUnsupported': 'errors.invoice.eInvoiceUnsupported',
  'Invoice.CompositionCannotCollectTax': 'errors.invoice.compositionCannotCollectTax',
  'Invoice.CompositionInterstateUnsupported': 'errors.invoice.compositionInterstateUnsupported',
  'Invoice.UnregisteredCannotCollectTax': 'errors.invoice.unregisteredCannotCollectTax',
  'Invoice.PlaceOfSupplyInvalid': 'errors.invoice.placeOfSupplyInvalid',
  'Invoice.PlaceOfSupplyRequired': 'errors.invoice.placeOfSupplyRequired',
  'Invoice.MixedSupplyToRegisteredBuyer': 'errors.invoice.mixedSupplyToRegisteredBuyer',
  'Invoice.BuyerDetailsRequired': 'errors.invoice.buyerDetailsRequired',
  'Invoice.HsnRequired': 'errors.invoice.hsnRequired',
  'Invoice.AmountsDoNotReconcile': 'errors.invoice.amountsDoNotReconcile',
  'Invoice.SellerGstinInvalid': 'errors.invoice.sellerGstinInvalid',
  'Invoice.SeriesRegisteredToAnotherShop': 'errors.invoice.seriesRegisteredToAnotherShop',
  'Invoice.SeriesRegistrationChanged': 'errors.invoice.seriesRegistrationChanged',
  'Invoice.SeriesExhausted': 'errors.invoice.seriesExhausted',
};

/** The message key for an invoice error code, or null when the code is not an invoice refusal this UI explains. */
export function invoiceErrorKey(code: unknown): string | null {
  return typeof code === 'string' ? (INVOICE_ERROR_KEYS[code] ?? null) : null;
}

