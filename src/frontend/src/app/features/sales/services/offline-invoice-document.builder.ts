import { gstinStateCode, isValidGstin } from '../../../shared/forms/input-validation';
import { gstStateName, isUnionTerritoryWithoutLegislature } from '../../../shared/gst/gst-state-codes';
import type { OfflineCustomerLiteSnapshot } from '../utils/offline-sales-snapshot.schema';
import { MONEY_SCALE, fromScaled, roundDiv, toScaled } from './offline-money.math';
import type { OfflineFrozenSaleLine, OfflineFrozenSalePricing } from './offline-sale-core.types';
import type {
  InvoiceDeclaration,
  InvoiceDocument,
  InvoiceDocumentBuyer,
  InvoiceDocumentLine,
  InvoiceDocumentSeller,
  InvoiceDocumentType,
  InvoiceEInvoiceStatus,
  InvoiceSellerProfile,
  TaxComponent,
  TaxComponentKind,
} from './sale.models';

// The device's copy of the server's InvoiceDocumentBuilder.Build. A queued offline sale prints the document built
// here, and the server issues its own from the same lease profile on sync, so the two must agree; both are pinned by
// tests/contracts/offline-invoice-document-v2.json. Refusals carry the server's error codes.

export const TAX_CALCULATION = 'component-split/1';
const GOODS_UOM = 'NOS';
const SERVICE_UOM = 'NA';
const IST_OFFSET_MS = 330 * 60 * 1000;
/** Rule 46(e): an unregistered buyer's name, address and state are required at this taxable value (paise). */
const UNREGISTERED_BUYER_DETAILS_THRESHOLD_PAISE = 5_000_000n;
const COMPONENT_ORDER: readonly TaxComponentKind[] = ['Cgst', 'Sgst', 'Utgst', 'Igst', 'Cess'];

/** Everything but the number, which is consumed from the lease only once the document is known to be issuable. */
export type OfflineInvoiceDocumentDraft = Omit<InvoiceDocument, 'number'>;

export interface OfflineInvoiceDocumentInput {
  readonly sellerProfile: InvoiceSellerProfile;
  readonly fiscalYearStart: number;
  readonly soldAt: string;
  /** The snapshot's record of the sale's customer, or null for a walk-in. */
  readonly customer: OfflineCustomerLiteSnapshot | null;
  readonly customerName: string | null;
  readonly customerPhone: string | null;
  readonly pricing: OfflineFrozenSalePricing;
}

export type OfflineInvoiceDocumentResult =
  | { readonly ok: true; readonly draft: OfflineInvoiceDocumentDraft }
  | { readonly ok: false; readonly errorCode: string };

interface PlaceOfSupply {
  readonly code: string;
  readonly name: string;
}

export function buildOfflineInvoiceDocument(input: OfflineInvoiceDocumentInput): OfflineInvoiceDocumentResult {
  const { sellerProfile: profile, pricing } = input;
  const seller = profile.seller;
  if (seller.taxRegime === 'Regular' && profile.eInvoiceStatus === 'Required') {
    return refuse('Invoice.EInvoiceUnsupported');
  }

  const lines = pricing.lines;
  if (!reconciles(pricing)) return refuse('Invoice.AmountsDoNotReconcile');

  const buyer = buildBuyer(input);
  if (!buyer) return refuse('Invoice.BuyerDetailsRequired');

  const place = resolvePlaceOfSupply(seller, buyer, lines);
  if ('errorCode' in place) return refuse(place.errorCode);

  const isInterstate = place.code !== seller.stateCode;
  const collectsTax = lines.some((line) => line.taxAmount !== 0 || line.taxRatePercent !== 0);
  const declarations: InvoiceDeclaration[] = [];
  let documentType: InvoiceDocumentType;

  switch (seller.taxRegime) {
    case 'Composition':
      if (collectsTax) return refuse('Invoice.CompositionCannotCollectTax');
      if (isInterstate) return refuse('Invoice.CompositionInterstateUnsupported');
      documentType = 'BillOfSupply';
      declarations.push('CompositionTaxablePerson');
      break;

    case 'Unregistered':
      if (collectsTax) return refuse('Invoice.UnregisteredCannotCollectTax');
      documentType = 'Receipt';
      declarations.push('NotRegisteredUnderGst');
      break;

    case 'Regular': {
      const taxableLines = lines.filter((line) => line.taxRatePercent > 0).length;
      documentType =
        taxableLines === lines.length ? 'TaxInvoice' : taxableLines === 0 ? 'BillOfSupply' : 'InvoiceCumBillOfSupply';

      // Rule 46A allows one invoice-cum-bill only to an unregistered recipient.
      if (documentType === 'InvoiceCumBillOfSupply' && buyer.gstin !== null) {
        return refuse('Invoice.MixedSupplyToRegisteredBuyer');
      }

      const particulars = checkRegularParticulars(profile.eInvoiceStatus, documentType, buyer, lines);
      if (particulars) return refuse(particulars);
      if (profile.eInvoiceStatus === 'Exempt') declarations.push('EInvoiceExemption');
      break;
    }

    default:
      return refuse('Invoice.TaxProfileRequired');
  }

  const unionTerritoryTax = isUnionTerritoryWithoutLegislature(place.code);
  const documentLines: InvoiceDocumentLine[] = lines.map((line, index) => {
    const isExempt = seller.taxRegime === 'Regular' && line.taxRatePercent === 0;
    const taxComponents =
      seller.taxRegime === 'Regular' && !isExempt
        ? splitTax(line.taxableAmount, line.taxRatePercent, line.taxAmount, isInterstate, unionTerritoryTax)
        : [];

    return {
      lineNumber: index + 1,
      description: line.itemName,
      hsnOrSac: line.hsnCode,
      uom: line.lineType === 'service' ? SERVICE_UOM : (line.documentUnit ?? GOODS_UOM),
      quantity: line.quantity,
      unitPrice: line.salesPrice,
      discountAmount: addMoney(line.itemDiscountAmount, line.saleDiscountAmount),
      taxableValue: line.taxableAmount,
      taxRatePercent: line.taxRatePercent,
      isExempt,
      taxComponents,
      lineTotal: line.lineTotal,
    };
  });

  return {
    ok: true,
    draft: {
      version: 2,
      documentType,
      issuedAt: input.soldAt,
      businessDate: indiaBusinessDate(input.soldAt),
      fiscalYearStart: input.fiscalYearStart,
      seriesCode: profile.seriesCode,
      seller: { ...seller },
      buyer,
      placeOfSupplyStateCode: place.code,
      placeOfSupplyStateName: place.name,
      isInterstate,
      reverseCharge: false,
      lines: documentLines,
      totals: {
        taxableValue: sumMoney(documentLines.filter((line) => !line.isExempt).map((line) => line.taxableValue)),
        exemptValue: sumMoney(documentLines.filter((line) => line.isExempt).map((line) => line.taxableValue)),
        components: groupComponents(documentLines.flatMap((line) => line.taxComponents)),
        totalTax: pricing.totals.totalTax,
        roundingAdjustment: 0,
        invoiceTotal: pricing.totals.grandTotal,
      },
      declarations,
      signatureMode: 'AuthorisedSignatory',
      taxCalculation: TAX_CALCULATION,
    },
  };
}

/**
 * `component-split/1`: intrastate tax splits into equal-rate central and state (or UT) halves, the odd paisa going to
 * CGST; interstate tax is IGST at the full rate.
 */
export function splitTax(
  taxableValue: number,
  ratePercent: number,
  taxAmount: number,
  isInterstate: boolean,
  unionTerritoryTax: boolean,
): TaxComponent[] {
  if (isInterstate) {
    return [{ kind: 'Igst', ratePercent, taxableBase: taxableValue, amount: taxAmount }];
  }

  const taxPaise = toScaled(taxAmount, MONEY_SCALE);
  const centralPaise = roundDiv(taxPaise, 2n);
  const halfRate = ratePercent / 2;
  return [
    { kind: 'Cgst', ratePercent: halfRate, taxableBase: taxableValue, amount: fromScaled(centralPaise, MONEY_SCALE) },
    {
      kind: unionTerritoryTax ? 'Utgst' : 'Sgst',
      ratePercent: halfRate,
      taxableBase: taxableValue,
      amount: fromScaled(taxPaise - centralPaise, MONEY_SCALE),
    },
  ];
}

/** The India business date (YYYY-MM-DD) of an instant, as the server's BusinessCalendar reads it. */
export function indiaBusinessDate(instant: string): string {
  return new Date(Date.parse(instant) + IST_OFFSET_MS).toISOString().slice(0, 10);
}

/** The fiscal year's first calendar year from a lease label such as "2026-27". */
export function fiscalYearStartOf(label: string): number | null {
  const match = /^(\d{4})-\d{2}(?:\d{2})?$/.exec(label.trim());
  return match ? Number(match[1]) : null;
}

function refuse(errorCode: string): OfflineInvoiceDocumentResult {
  return { ok: false, errorCode };
}

function reconciles(pricing: OfflineFrozenSalePricing): boolean {
  const paise = (value: number) => toScaled(value, MONEY_SCALE);
  let tax = 0n;
  let total = 0n;
  for (const line of pricing.lines) {
    if (paise(line.lineTotal) !== paise(line.taxableAmount) + paise(line.taxAmount)) return false;
    tax += paise(line.taxAmount);
    total += paise(line.lineTotal);
  }

  return tax === paise(pricing.totals.totalTax) && total === paise(pricing.totals.grandTotal);
}

/** The buyer as recorded; null when the customer's GSTIN is unusable. */
function buildBuyer(input: OfflineInvoiceDocumentInput): InvoiceDocumentBuyer | null {
  const customer = input.customer;
  const gstin = normalizeGstin(customer?.gstin);
  if (gstin !== null && !isValidGstin(gstin)) return null;

  const stateCode = customer?.stateCode ?? (gstin === null ? null : gstinStateCode(gstin));
  const stateName = gstStateName(stateCode);
  return {
    name: customer?.name ?? input.customerName,
    phone: customer?.phoneNumber ?? input.customerPhone,
    address: customer?.address ?? null,
    gstin,
    stateCode: stateName ? stateCode : null,
    stateName,
  };
}

/**
 * The place of supply of an offline sale, which never states one: goods handed over at the counter are supplied in
 * the seller's state; services go to the recipient's recorded state, or the seller's when none is on record. A sale
 * holding both whose places differ has no single place of supply.
 */
function resolvePlaceOfSupply(
  seller: InvoiceDocumentSeller,
  buyer: InvoiceDocumentBuyer,
  lines: readonly OfflineFrozenSaleLine[],
): PlaceOfSupply | { readonly errorCode: string } {
  const hasServices = lines.some((line) => line.lineType === 'service');
  const hasGoods = lines.some((line) => line.lineType !== 'service');
  const servicesStateCode = buyer.stateCode ?? seller.stateCode;
  if (hasServices && hasGoods && servicesStateCode !== seller.stateCode) {
    return { errorCode: 'Invoice.PlaceOfSupplyRequired' };
  }

  const code = hasServices ? servicesStateCode : seller.stateCode;
  const name = gstStateName(code);
  return name ? { code, name } : { errorCode: 'Invoice.PlaceOfSupplyInvalid' };
}

function checkRegularParticulars(
  eInvoiceStatus: InvoiceEInvoiceStatus,
  documentType: InvoiceDocumentType,
  buyer: InvoiceDocumentBuyer,
  lines: readonly OfflineFrozenSaleLine[],
): string | null {
  const isInvoice = documentType === 'TaxInvoice' || documentType === 'InvoiceCumBillOfSupply';
  const isRegistered = buyer.gstin !== null;
  if (isRegistered) {
    // Rule 46(d): a registered recipient's name, address and GSTIN.
    if (isBlank(buyer.name) || isBlank(buyer.address)) return 'Invoice.BuyerDetailsRequired';
  } else if (isInvoice) {
    const taxable = lines.reduce((sum, line) => sum + toScaled(line.taxableAmount, MONEY_SCALE), 0n);
    // Rule 46(e): an unregistered recipient's name, address and state at ₹50,000 or more.
    if (
      taxable >= UNREGISTERED_BUYER_DETAILS_THRESHOLD_PAISE &&
      (isBlank(buyer.name) || isBlank(buyer.address) || buyer.stateCode === null)
    ) {
      return 'Invoice.BuyerDetailsRequired';
    }
  }

  // HSN digits (Notification 78/2020): an e-invoice-exempt registration needs six digits on every line; otherwise
  // four on B2B invoices.
  const minimumDigits = eInvoiceStatus === 'Exempt' ? 6 : isRegistered && isInvoice ? 4 : 0;
  if (minimumDigits > 0 && lines.some((line) => !hasHsnDigits(line.hsnCode, minimumDigits))) {
    return 'Invoice.HsnRequired';
  }

  return null;
}

function hasHsnDigits(code: string | null, minimumDigits: number): boolean {
  return !!code && [4, 6, 8].includes(code.length) && code.length >= minimumDigits && /^[0-9]+$/.test(code);
}

/** One component per kind and rate, ordered as the server orders them: by kind, then by rate. */
function groupComponents(components: readonly TaxComponent[]): TaxComponent[] {
  const grouped = new Map<string, { kind: TaxComponentKind; ratePercent: number; base: bigint; amount: bigint }>();
  for (const component of components) {
    const key = `${component.kind}|${component.ratePercent}`;
    const current = grouped.get(key) ?? { kind: component.kind, ratePercent: component.ratePercent, base: 0n, amount: 0n };
    current.base += toScaled(component.taxableBase, MONEY_SCALE);
    current.amount += toScaled(component.amount, MONEY_SCALE);
    grouped.set(key, current);
  }

  return [...grouped.values()]
    .sort(
      (left, right) =>
        COMPONENT_ORDER.indexOf(left.kind) - COMPONENT_ORDER.indexOf(right.kind) || left.ratePercent - right.ratePercent,
    )
    .map((group) => ({
      kind: group.kind,
      ratePercent: group.ratePercent,
      taxableBase: fromScaled(group.base, MONEY_SCALE),
      amount: fromScaled(group.amount, MONEY_SCALE),
    }));
}

function addMoney(left: number, right: number): number {
  return sumMoney([left, right]);
}

function sumMoney(values: readonly number[]): number {
  return fromScaled(
    values.reduce((sum, value) => sum + toScaled(value, MONEY_SCALE), 0n),
    MONEY_SCALE,
  );
}

function normalizeGstin(value: string | null | undefined): string | null {
  const trimmed = value?.trim() ?? '';
  return trimmed.length > 0 ? trimmed.toUpperCase() : null;
}

function isBlank(value: string | null): boolean {
  return !value || value.trim().length === 0;
}
