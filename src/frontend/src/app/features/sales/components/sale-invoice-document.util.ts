import type {
  InvoiceDeclaration,
  InvoiceDocument,
  InvoiceDocumentLine,
  InvoiceDocumentType,
  SaleDto,
  TaxComponent,
  TaxComponentKind,
} from '../services/sale.models';
import { getPaymentMethodLabel } from '../services/sale.models';

// Shared by the A4 and thermal prints. An issued document is rendered only from its snapshot; the live shop is used
// for legacy sales alone.

const DOCUMENT_TITLE_KEYS: Readonly<Record<InvoiceDocumentType, string>> = {
  TaxInvoice: 'sales.invoice.documentTypes.taxInvoice',
  InvoiceCumBillOfSupply: 'sales.invoice.documentTypes.invoiceCumBillOfSupply',
  BillOfSupply: 'sales.invoice.documentTypes.billOfSupply',
  Receipt: 'sales.invoice.documentTypes.receipt',
};

const DECLARATION_KEYS: Readonly<Record<InvoiceDeclaration, string>> = {
  CompositionTaxablePerson: 'sales.invoice.declarations.compositionTaxablePerson',
  EInvoiceExemption: 'sales.invoice.declarations.eInvoiceExemption',
  NotRegisteredUnderGst: 'sales.invoice.declarations.notRegisteredUnderGst',
};

/** Statutory abbreviations, printed as they are on every GST document whatever its language. */
const TAX_COMPONENT_ABBREVIATIONS: Readonly<Record<TaxComponentKind, string>> = {
  Cgst: 'CGST',
  Sgst: 'SGST',
  Utgst: 'UTGST',
  Igst: 'IGST',
  Cess: 'Cess',
};

const TAX_COMPONENT_ORDER: readonly TaxComponentKind[] = ['Cgst', 'Sgst', 'Utgst', 'Igst', 'Cess'];

export interface TaxSummaryRow {
  readonly kind: TaxComponentKind;
  readonly abbreviation: string;
  readonly ratePercent: number;
  readonly taxableBase: number;
  readonly amount: number;
}

export type PaymentStatusKey = 'sales.invoice.paid' | 'sales.invoice.partiallyPaid' | 'sales.invoice.unpaid';

export function documentTitleKey(document: InvoiceDocument): string {
  return DOCUMENT_TITLE_KEYS[document.documentType] ?? 'sales.invoice.title';
}

export function declarationKey(declaration: InvoiceDeclaration): string {
  return DECLARATION_KEYS[declaration] ?? 'shops.unknown';
}

export function hasDistinctTradeName(document: InvoiceDocument): boolean {
  const tradeName = document.seller.tradeName?.trim() ?? '';
  return tradeName.length > 0 && tradeName !== document.seller.legalName.trim();
}

export function lineTaxAmount(line: InvoiceDocumentLine): number {
  return roundPaise(line.taxComponents.reduce((sum, component) => sum + component.amount, 0));
}

/** One row per component kind and rate, so CGST 9% and CGST 2.5% stay apart and repeated rows are added up. */
export function groupTaxComponents(components: readonly TaxComponent[]): TaxSummaryRow[] {
  const rows = new Map<string, TaxSummaryRow>();
  for (const component of components) {
    const key = `${component.kind}|${component.ratePercent}`;
    const current = rows.get(key);
    rows.set(key, {
      kind: component.kind,
      abbreviation: TAX_COMPONENT_ABBREVIATIONS[component.kind] ?? component.kind,
      ratePercent: component.ratePercent,
      taxableBase: roundPaise((current?.taxableBase ?? 0) + component.taxableBase),
      amount: roundPaise((current?.amount ?? 0) + component.amount),
    });
  }

  return [...rows.values()].sort(
    (left, right) =>
      TAX_COMPONENT_ORDER.indexOf(left.kind) - TAX_COMPONENT_ORDER.indexOf(right.kind) ||
      right.ratePercent - left.ratePercent,
  );
}

/** Codes of the credit notes redeemed against this sale (not the ones its returns issued). */
export function redeemedCreditNoteCodes(sale: SaleDto): string[] {
  return (sale.creditNoteRedemptions ?? [])
    .map((redemption) => redemption.code?.trim() ?? '')
    .filter((code) => code.length > 0);
}

export function paymentStatusKey(sale: SaleDto): PaymentStatusKey {
  if (sale.dueAmount <= 0) return 'sales.invoice.paid';
  // A redeemed credit note settles part of the bill just as a payment does.
  const settled = sale.paidAmount + (sale.creditNoteAppliedAmount ?? 0);
  return settled > 0 ? 'sales.invoice.partiallyPaid' : 'sales.invoice.unpaid';
}

/**
 * The note a document-less print carries. The server reports every sale without a document as legacy, whether it
 * predates GST documents or was an offline sale posted without one, so the note states only what is true of both.
 * A queued offline sale has no server document yet and carries no note.
 */
export function missingDocumentNoteKey(sale: SaleDto, pendingSync: boolean): string | null {
  if (pendingSync || sale.document) return null;
  return 'sales.invoice.noDocumentNote';
}

function roundPaise(value: number): number {
  return Math.round(value * 100) / 100;
}

const PAYMENT_METHOD_LABEL_KEYS: Readonly<Record<string, string>> = {
  cash: 'sales.newSale.paymentMethods.cash',
  upi: 'sales.newSale.paymentMethods.upi',
  card: 'sales.newSale.paymentMethods.card',
  credit: 'sales.newSale.paymentMethods.credit',
};

/** The label key for a payment method, sent by the API by name ("Cash") and by older records as its number. */
export function paymentMethodLabelKey(method: number | string): string {
  const name = typeof method === 'string' ? method : getPaymentMethodLabel(method);
  return PAYMENT_METHOD_LABEL_KEYS[name.toLowerCase()] ?? 'shops.unknown';
}
