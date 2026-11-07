import { describe, expect, it } from 'vitest';
import { validShipmentQuantity, validCreditAmount } from './supplier-return-page.component';
describe('Supplier return editor', () => {
  it('accepts canonical paise without binary multiplication errors', () => {
    for (const value of [0, 0.29, 1.1, 2.3, 99999999.99])
      expect(validCreditAmount(value)).toBe(true);
    for (const value of [-1, 0.001, 1.101, NaN, Infinity, 100000000])
      expect(validCreditAmount(value)).toBe(false);
  });
  it('limits positive available quantity and item precision', () => {
    expect(validShipmentQuantity(3, 6, 0)).toBe(true);
    expect(validShipmentQuantity(7, 6, 0)).toBe(false);
    expect(validShipmentQuantity(0.1, 6, 0)).toBe(false);
    expect(validShipmentQuantity(0.125, 6, 3)).toBe(true);
    expect(validShipmentQuantity(0, 6, 3)).toBe(false);
  });
});
