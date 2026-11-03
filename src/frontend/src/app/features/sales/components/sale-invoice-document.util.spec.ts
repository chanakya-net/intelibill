import { describe, expect, it } from 'vitest';

import type { SaleDto } from '../services/sale.models';
import { missingDocumentNoteKey, paymentMethodLabelKey, paymentStatusKey } from './sale-invoice-document.util';

function sale(overrides: Partial<SaleDto>): SaleDto {
  return { paidAmount: 0, dueAmount: 0, creditNoteAppliedAmount: 0, document: null, isLegacyDocument: true, ...overrides } as SaleDto;
}

describe('sale invoice document helpers', () => {
  it('labels the payment method whether the API sends its name or an older record its number', () => {
    expect(paymentMethodLabelKey('Cash')).toBe('sales.newSale.paymentMethods.cash');
    expect(paymentMethodLabelKey('UPI')).toBe('sales.newSale.paymentMethods.upi');
    expect(paymentMethodLabelKey(3)).toBe('sales.newSale.paymentMethods.card');
    expect(paymentMethodLabelKey('Barter')).toBe('shops.unknown');
  });

  it('counts a redeemed credit note as part payment', () => {
    expect(paymentStatusKey(sale({ dueAmount: 0 }))).toBe('sales.invoice.paid');
    expect(paymentStatusKey(sale({ dueAmount: 136, creditNoteAppliedAmount: 100 }))).toBe('sales.invoice.partiallyPaid');
    expect(paymentStatusKey(sale({ dueAmount: 136, paidAmount: 10 }))).toBe('sales.invoice.partiallyPaid');
    expect(paymentStatusKey(sale({ dueAmount: 136 }))).toBe('sales.invoice.unpaid');
  });

  it('notes a missing document only on a synced sale without one', () => {
    expect(missingDocumentNoteKey(sale({}), false)).toBe('sales.invoice.noDocumentNote');
    expect(missingDocumentNoteKey(sale({}), true)).toBeNull();
  });
});
