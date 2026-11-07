import { describe, expect, it } from 'vitest';
import { validShipmentQuantity } from './supplier-return-page.component';
describe('Supplier return editor', () => {
  it('limits positive available quantity and item precision', () => {
    expect(validShipmentQuantity(3, 6, 0)).toBe(true);
    expect(validShipmentQuantity(7, 6, 0)).toBe(false);
    expect(validShipmentQuantity(0.1, 6, 0)).toBe(false);
    expect(validShipmentQuantity(0.125, 6, 3)).toBe(true);
    expect(validShipmentQuantity(0, 6, 3)).toBe(false);
  });
});
