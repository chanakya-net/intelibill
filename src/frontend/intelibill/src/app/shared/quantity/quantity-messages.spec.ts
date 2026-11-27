import { describe, expect, it } from 'vitest';

import { entryScaleOf, quantityPrecisionMessage } from './quantity-messages';

describe('quantityPrecisionMessage', () => {
  it('is null for a quantity that fits the scale', () => {
    expect(quantityPrecisionMessage(0.125, 'kg', 3, 'en')).toBeNull();
    expect(quantityPrecisionMessage(2, 'PCS', 0, 'en')).toBeNull();
    expect(quantityPrecisionMessage(null, 'PCS', 0, 'en')).toBeNull();
  });

  it('names the unit and decimals for a whole-unit item', () => {
    expect(quantityPrecisionMessage(0.5, 'PCS', 0, 'en')).toEqual({
      key: 'common.quantity.errors.tooPreciseWhole',
      params: { unit: 'PCS', decimals: 0, step: '1' },
    });
  });

  it('names the unit and decimals for a fractional item', () => {
    expect(quantityPrecisionMessage(0.25, 'Bundle', 1, 'en')).toEqual({
      key: 'common.quantity.errors.tooPrecise',
      params: { unit: 'Bundle', decimals: 1, step: '0.1' },
    });
  });

  it('falls back to the storage ceiling when the scale is unknown', () => {
    expect(entryScaleOf(undefined)).toBe(3);
    expect(entryScaleOf(null)).toBe(3);
    expect(quantityPrecisionMessage(0.0001, 'kg', undefined, 'en')?.params.decimals).toBe(3);
  });
});
