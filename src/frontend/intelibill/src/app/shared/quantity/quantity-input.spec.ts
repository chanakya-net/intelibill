import { describe, expect, it } from 'vitest';

import {
  fitsQuantityScale,
  formatQuantity,
  normalizeQuantity,
  parseQuantityInput,
  quantityScaleOf,
  quantityStep,
} from './quantity-input';

describe('parseQuantityInput', () => {
  it('accepts an exact fraction within the unit scale', () => {
    expect(parseQuantityInput('0.25', 'en-IN', 3)).toEqual({ ok: true, value: 0.25 });
    expect(parseQuantityInput('0.001', 'en-IN', 3)).toEqual({ ok: true, value: 0.001 });
    expect(parseQuantityInput('1.5', 'en-IN', 1)).toEqual({ ok: true, value: 1.5 });
    expect(parseQuantityInput('40', 'en-IN', 0)).toEqual({ ok: true, value: 40 });
  });

  it('accepts a pasted value with surrounding whitespace', () => {
    expect(parseQuantityInput(' 0.25\n', 'en-IN', 3)).toEqual({ ok: true, value: 0.25 });
  });

  it('does not count trailing zeros as precision', () => {
    expect(parseQuantityInput('2.000', 'en-IN', 0)).toEqual({ ok: true, value: 2 });
    expect(parseQuantityInput('0.250', 'en-IN', 2)).toEqual({ ok: true, value: 0.25 });
  });

  it('refuses a comma in en-IN, where it is the grouping separator', () => {
    expect(parseQuantityInput('1,5', 'en-IN', 3)).toEqual({ ok: false, error: 'invalid' });
  });

  it('accepts the locale decimal separator besides the point', () => {
    expect(parseQuantityInput('1,5', 'de-DE', 3)).toEqual({ ok: true, value: 1.5 });
    expect(parseQuantityInput('1.5', 'de-DE', 3)).toEqual({ ok: true, value: 1.5 });
  });

  it('reports an empty field', () => {
    expect(parseQuantityInput('', 'en-IN', 0)).toEqual({ ok: false, error: 'empty' });
    expect(parseQuantityInput('   ', 'en-IN', 0)).toEqual({ ok: false, error: 'empty' });
  });

  it('refuses zero and negative quantities', () => {
    expect(parseQuantityInput('0', 'en-IN', 0)).toEqual({ ok: false, error: 'notPositive' });
    expect(parseQuantityInput('0.000', 'en-IN', 3)).toEqual({ ok: false, error: 'notPositive' });
    expect(parseQuantityInput('-1', 'en-IN', 0)).toEqual({ ok: false, error: 'notPositive' });
    expect(parseQuantityInput('-0.5', 'en-IN', 3)).toEqual({ ok: false, error: 'notPositive' });
  });

  it('refuses anything that is not plain digits with one decimal separator', () => {
    for (const text of ['NaN', 'Infinity', '1e3', '1 000', '+1', '1.2.3', '0x10', '1,000.5', '.', 'abc', '--1', '1-', '½']) {
      expect(parseQuantityInput(text, 'en-IN', 3), text).toEqual({ ok: false, error: 'invalid' });
    }
  });

  it('refuses more decimals than the unit allows instead of rounding', () => {
    expect(parseQuantityInput('0.5', 'en-IN', 0)).toEqual({ ok: false, error: 'tooPrecise' });
    expect(parseQuantityInput('0.0001', 'en-IN', 3)).toEqual({ ok: false, error: 'tooPrecise' });
    expect(parseQuantityInput('1.25', 'en-IN', 1)).toEqual({ ok: false, error: 'tooPrecise' });
  });

  it('refuses more integer digits than storage holds', () => {
    expect(parseQuantityInput('1234567890123456', 'en-IN', 0)).toEqual({ ok: false, error: 'invalid' });
    expect(parseQuantityInput('123456789012345', 'en-IN', 0)).toEqual({ ok: true, value: 123456789012345 });
  });
});

describe('quantity scale helpers', () => {
  it('gives one step of the unit precision', () => {
    expect(quantityStep(0)).toBe(1);
    expect(quantityStep(1)).toBe(0.1);
    expect(quantityStep(2)).toBe(0.01);
    expect(quantityStep(3)).toBe(0.001);
  });

  it('checks a value against a scale without rounding it', () => {
    expect(fitsQuantityScale(2, 0)).toBe(true);
    expect(fitsQuantityScale(0.5, 0)).toBe(false);
    expect(fitsQuantityScale(0.25, 3)).toBe(true);
    expect(fitsQuantityScale(0.0001, 3)).toBe(false);
    expect(fitsQuantityScale(0.1 + 0.2, 1)).toBe(false);
    expect(fitsQuantityScale(Number.NaN, 3)).toBe(false);
    expect(fitsQuantityScale(Number.POSITIVE_INFINITY, 3)).toBe(false);
  });

  it('finds the fewest decimal places that hold a quantity', () => {
    expect(quantityScaleOf(2)).toBe(0);
    expect(quantityScaleOf(0.5)).toBe(1);
    expect(quantityScaleOf(0.25)).toBe(2);
    expect(quantityScaleOf(0.001)).toBe(3);
    expect(quantityScaleOf(Number.NaN)).toBe(0);
  });

  it('normalizes float noise from arithmetic on on-scale values', () => {
    expect(normalizeQuantity(0.1 + 0.2, 1)).toBe(0.3);
    expect(normalizeQuantity(1.1 - 1, 1)).toBe(0.1);
    expect(normalizeQuantity(1.001 - 1, 3)).toBe(0.001);
    expect(normalizeQuantity(40, 0)).toBe(40);
  });

  it('formats without grouping, in Latin digits, never rounding a value', () => {
    expect(formatQuantity(0.25, 3, 'en-IN')).toBe('0.25');
    expect(formatQuantity(1500, 0, 'en-IN')).toBe('1500');
    expect(formatQuantity(1.5, 3, 'mr-IN')).toBe('1.5');
    expect(formatQuantity(2.5, 0, 'en-IN')).toBe('2.5');
    expect(formatQuantity(1.5, 3, 'de-DE')).toBe('1,5');
  });

  it('formats with an unknown locale tag by falling back to the point', () => {
    expect(formatQuantity(0.75, 3, 'not a locale')).toBe('0.75');
    expect(parseQuantityInput('0.75', 'not a locale', 3)).toEqual({ ok: true, value: 0.75 });
  });
});
