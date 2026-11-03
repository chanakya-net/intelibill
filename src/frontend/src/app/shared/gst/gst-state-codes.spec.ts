import { GST_STATES, GST_STATE_OPTIONS, gstStateName, isKnownGstStateCode } from './gst-state-codes';

describe('GST state codes', () => {
  it('lists every GST state and union territory once, in code order', () => {
    const codes = GST_STATES.map((state) => state.code);

    expect(codes).toHaveLength(36);
    expect(new Set(codes).size).toBe(codes.length);
    expect([...codes].sort()).toEqual(codes);
    expect(codes).not.toContain('25');
    expect(codes).not.toContain('28');
  });

  it('names a known code and refuses unknown or non-state codes', () => {
    expect(gstStateName('27')).toBe('Maharashtra');
    expect(gstStateName('04')).toBe('Chandigarh');
    expect(gstStateName('26')).toBe('Dadra and Nagar Haveli and Daman and Diu');
    expect(gstStateName('38')).toBe('Ladakh');
    expect(gstStateName('97')).toBeNull();
    expect(gstStateName(null)).toBeNull();

    expect(isKnownGstStateCode('33')).toBe(true);
    expect(isKnownGstStateCode('96')).toBe(false);
    expect(isKnownGstStateCode('')).toBe(false);
  });

  it('offers a select option per state showing its name and code', () => {
    expect(GST_STATE_OPTIONS).toHaveLength(GST_STATES.length);
    expect(GST_STATE_OPTIONS.find((option) => option.value === '29')).toEqual({ value: '29', name: 'Karnataka (29)' });
  });
});
