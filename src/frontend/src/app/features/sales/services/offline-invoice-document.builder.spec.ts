import { describe, expect, it } from 'vitest';

import contract from '../../../../../../../tests/contracts/offline-invoice-document-v2.json';
import type { OfflineCustomerLiteSnapshot } from '../utils/offline-sales-snapshot.schema';
import type { OfflineFrozenSaleLine, OfflineFrozenSalePricing } from './offline-sale-core.types';
import { buildOfflineInvoiceDocument, fiscalYearStartOf, indiaBusinessDate, splitTax } from './offline-invoice-document.builder';
import type { InvoiceSellerProfile, TaxComponent } from './sale.models';

interface ContractLine {
  readonly lineType: string;
  readonly description: string;
  readonly hsn: string | null;
  readonly documentUnit?: string;
  readonly quantity: number;
  readonly unitPrice: number;
  readonly discountAmount: number;
  readonly taxableAmount: number;
  readonly taxRatePercent: number;
  readonly taxAmount: number;
  readonly lineTotal: number;
}

interface ContractCustomer {
  readonly name: string;
  readonly phone: string;
  readonly address: string | null;
  readonly gstin: string | null;
  readonly stateCode: string | null;
}

type ContractComponent = readonly (string | number)[];

interface ContractExpected {
  readonly documentType: string;
  readonly isInterstate: boolean;
  readonly placeOfSupplyStateCode: string;
  readonly placeOfSupplyStateName: string;
  readonly buyer: Record<string, string | null>;
  readonly declarations: readonly string[];
  readonly lines: readonly { readonly uom: string; readonly isExempt: boolean; readonly components: readonly ContractComponent[] }[];
  readonly totals: {
    readonly taxableValue: number;
    readonly exemptValue: number;
    readonly totalTax: number;
    readonly invoiceTotal: number;
    readonly components: readonly ContractComponent[];
  };
}

interface ContractCase {
  readonly name: string;
  readonly seller: string;
  readonly customer: ContractCustomer | null;
  readonly saleCustomerName: string | null;
  readonly saleCustomerPhone: string | null;
  readonly lines: readonly ContractLine[];
  readonly expected?: ContractExpected;
  readonly expectedError?: string;
}

const cases = contract.cases as unknown as readonly ContractCase[];
const sellers = contract.sellers as unknown as Record<string, InvoiceSellerProfile>;

function frozenLine(line: ContractLine, index: number): OfflineFrozenSaleLine {
  return {
    clientLineId: `line-${index}`,
    inventoryBatchId: `batch-${index}`,
    itemId: `item-${index}`,
    barcode: 'CODE',
    itemName: line.description,
    batchNumber: '',
    quantity: line.quantity,
    salesPrice: line.unitPrice,
    mrp: line.unitPrice,
    costPrice: 0,
    taxRatePercent: line.taxRatePercent,
    taxIncluded: false,
    hsnCode: line.hsn,
    preTaxAmount: line.quantity * line.unitPrice,
    itemDiscountAmount: line.discountAmount,
    saleDiscountAmount: 0,
    taxableAmount: line.taxableAmount,
    taxAmount: line.taxAmount,
    lineTotal: line.lineTotal,
    configuredRuleId: null,
    ...(line.documentUnit ? { documentUnit: line.documentUnit } : {}),
    lineType: line.lineType === 'service' ? 'service' : 'goods',
    serviceId: line.lineType === 'service' ? `service-${index}` : null,
  };
}

function pricingOf(lines: readonly ContractLine[]): OfflineFrozenSalePricing {
  const grandTotal = lines.reduce((sum, line) => sum + line.lineTotal, 0);
  return {
    calculationVersion: 1,
    lines: lines.map(frozenLine),
    totals: {
      subtotalBeforeDiscount: lines.reduce((sum, line) => sum + line.quantity * line.unitPrice, 0),
      totalBeforeDiscount: lines.reduce((sum, line) => sum + line.quantity * line.unitPrice, 0),
      totalDiscount: lines.reduce((sum, line) => sum + line.discountAmount, 0),
      totalTax: lines.reduce((sum, line) => sum + line.taxAmount, 0),
      grandTotal,
      paidAmount: grandTotal,
      dueAmount: 0,
    },
  };
}

function customerOf(customer: ContractCustomer | null): OfflineCustomerLiteSnapshot | null {
  return customer
    ? {
        customerId: 'customer-1',
        name: customer.name,
        phoneNumber: customer.phone,
        address: customer.address,
        gstin: customer.gstin,
        stateCode: customer.stateCode,
      }
    : null;
}

function components(list: readonly TaxComponent[]): ContractComponent[] {
  return list.map((component) => [component.kind, component.ratePercent, component.taxableBase, component.amount]);
}

describe('buildOfflineInvoiceDocument', () => {
  it.each(cases.map((testCase) => [testCase.name, testCase] as const))('%s', (_name, testCase) => {
    const result = buildOfflineInvoiceDocument({
      sellerProfile: sellers[testCase.seller],
      fiscalYearStart: contract.fiscalYearStart,
      soldAt: contract.issuedAt,
      customer: customerOf(testCase.customer),
      customerName: testCase.saleCustomerName,
      customerPhone: testCase.saleCustomerPhone,
      pricing: pricingOf(testCase.lines),
    });

    if (testCase.expectedError) {
      expect(result).toEqual({ ok: false, errorCode: testCase.expectedError });
      return;
    }

    expect(result.ok).toBe(true);
    if (!result.ok) return;
    const draft = result.draft;
    const expected = testCase.expected!;
    const profile = sellers[testCase.seller];

    expect(draft.version).toBe(2);
    expect(draft.issuedAt).toBe(contract.issuedAt);
    expect(draft.businessDate).toBe(contract.businessDate);
    expect(draft.fiscalYearStart).toBe(contract.fiscalYearStart);
    expect(draft.seriesCode).toBe(profile.seriesCode);
    expect(draft.seller).toEqual(profile.seller);
    expect(draft.documentType).toBe(expected.documentType);
    expect(draft.isInterstate).toBe(expected.isInterstate);
    expect(draft.reverseCharge).toBe(false);
    expect(draft.placeOfSupplyStateCode).toBe(expected.placeOfSupplyStateCode);
    expect(draft.placeOfSupplyStateName).toBe(expected.placeOfSupplyStateName);
    expect(draft.buyer).toEqual(expected.buyer);
    expect(draft.declarations).toEqual(expected.declarations);
    expect(draft.signatureMode).toBe('AuthorisedSignatory');
    expect(draft.taxCalculation).toBe('component-split/1');

    expect(draft.lines).toHaveLength(expected.lines.length);
    draft.lines.forEach((line, index) => {
      const input = testCase.lines[index];
      expect(line).toMatchObject({
        lineNumber: index + 1,
        description: input.description,
        hsnOrSac: input.hsn,
        uom: expected.lines[index].uom,
        quantity: input.quantity,
        unitPrice: input.unitPrice,
        discountAmount: input.discountAmount,
        taxableValue: input.taxableAmount,
        taxRatePercent: input.taxRatePercent,
        isExempt: expected.lines[index].isExempt,
        lineTotal: input.lineTotal,
      });
      expect(components(line.taxComponents)).toEqual(expected.lines[index].components);
    });

    expect(draft.totals).toMatchObject({
      taxableValue: expected.totals.taxableValue,
      exemptValue: expected.totals.exemptValue,
      totalTax: expected.totals.totalTax,
      roundingAdjustment: 0,
      invoiceTotal: expected.totals.invoiceTotal,
    });
    expect(components(draft.totals.components)).toEqual(expected.totals.components);
  });

  it('refuses a lease profile that could not issue a document online', () => {
    const regular = sellers['regularKarnataka'];
    const input = {
      fiscalYearStart: 2026,
      soldAt: contract.issuedAt,
      customer: null,
      customerName: null,
      customerPhone: null,
      pricing: pricingOf(cases[0].lines),
    };

    expect(buildOfflineInvoiceDocument({ ...input, sellerProfile: { ...regular, eInvoiceStatus: 'Required' } })).toEqual({
      ok: false,
      errorCode: 'Invoice.EInvoiceUnsupported',
    });
  });
});

describe('splitTax', () => {
  it('gives CGST the odd paisa and keeps the half rates', () => {
    expect(splitTax(100.05, 18, 18.01, false, false)).toEqual([
      { kind: 'Cgst', ratePercent: 9, taxableBase: 100.05, amount: 9.01 },
      { kind: 'Sgst', ratePercent: 9, taxableBase: 100.05, amount: 9 },
    ]);
  });
});

describe('indiaBusinessDate', () => {
  it('reads the date in India, not UTC', () => {
    expect(indiaBusinessDate('2026-11-03T18:29:59Z')).toBe('2026-11-03');
    expect(indiaBusinessDate('2026-11-03T18:30:00Z')).toBe('2026-11-04');
  });
});

describe('fiscalYearStartOf', () => {
  it('reads the first year of a lease label and nothing else', () => {
    expect(fiscalYearStartOf('2026-27')).toBe(2026);
    expect(fiscalYearStartOf('2026-2027')).toBe(2026);
    expect(fiscalYearStartOf('FY26')).toBeNull();
  });
});
