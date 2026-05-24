import { NO_DISCOUNT, PAYMENT_METHOD_VALUES, SALE_RETURN_CONDITIONS } from './sale.models';

describe('sale.models', () => {
  describe('NO_DISCOUNT', () => {
    it('has type 0 (None)', () => {
      expect(NO_DISCOUNT.type).toBe(0);
    });

    it('has value 0', () => {
      expect(NO_DISCOUNT.value).toBe(0);
    });
  });

  describe('PAYMENT_METHOD_VALUES', () => {
    it('contains exactly 4 entries', () => {
      expect(PAYMENT_METHOD_VALUES).toHaveLength(4);
    });

    it('maps numeric values to expected labels', () => {
      expect(PAYMENT_METHOD_VALUES[0]).toEqual({ value: 1, label: 'Cash' });
      expect(PAYMENT_METHOD_VALUES[1]).toEqual({ value: 2, label: 'UPI' });
      expect(PAYMENT_METHOD_VALUES[2]).toEqual({ value: 3, label: 'Card' });
      expect(PAYMENT_METHOD_VALUES[3]).toEqual({ value: 4, label: 'Credit' });
    });

    it('has unique numeric values', () => {
      const values = PAYMENT_METHOD_VALUES.map((e) => e.value);
      expect(new Set(values).size).toBe(values.length);
    });
  });

  describe('SALE_RETURN_CONDITIONS', () => {
    it('contains exactly 2 entries', () => {
      expect(SALE_RETURN_CONDITIONS).toHaveLength(2);
    });

    it('maps condition values to expected labels', () => {
      expect(SALE_RETURN_CONDITIONS[0]).toEqual({ value: 1, label: 'Restockable' });
      expect(SALE_RETURN_CONDITIONS[1]).toEqual({ value: 2, label: 'Wastage' });
    });
  });
});
