import { describe, expect, it } from 'vitest';

import {
  formatLocalIsoDate,
  formatUtcIsoInstant,
  parseDateOnlyAsLocalDate,
} from './date-time.util';

describe('date-time utilities', () => {
  it('formats date-only values from local calendar fields', () => {
    const value = new Date(2026, 4, 15, 0, 30);

    expect(formatLocalIsoDate(value)).toBe('2026-05-15');
  });

  it('parses date-only values as local dates', () => {
    const value = parseDateOnlyAsLocalDate('2026-05-15');

    expect(value.getFullYear()).toBe(2026);
    expect(value.getMonth()).toBe(4);
    expect(value.getDate()).toBe(15);
  });

  it('formats instants as UTC ISO strings', () => {
    const value = new Date(2026, 4, 15, 9, 30);

    expect(formatUtcIsoInstant(value)).toBe(value.toISOString());
  });
});
