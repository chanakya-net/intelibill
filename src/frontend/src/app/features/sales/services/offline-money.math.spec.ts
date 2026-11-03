import { describe, expect, it } from 'vitest';

import { allocateLargestRemainder, fromScaled, normalizeScale, roundDiv, toScaled } from './offline-money.math';

describe('offline money math', () => {
  it('rounds integer division half away from zero', () => {
    expect(roundDiv(5n, 10n)).toBe(1n);
    expect(roundDiv(4n, 10n)).toBe(0n);
    expect(roundDiv(-5n, 10n)).toBe(-1n);
    expect(roundDiv(-4n, 10n)).toBe(0n);
    expect(roundDiv(15n, 10n)).toBe(2n);
  });

  it('parses the decimal text of a number instead of its binary value', () => {
    expect(toScaled(1.005, 2)).toBe(101n);
    expect(toScaled(0.1, 2)).toBe(10n);
    expect(toScaled(29.5, 2)).toBe(2950n);
    expect(toScaled(0.25, 3)).toBe(250n);
    expect(toScaled(-1.005, 2)).toBe(-101n);
    expect(toScaled(1e-7, 2)).toBe(0n);
    expect(toScaled(1.5e21, 2)).toBe(150000000000000000000000n);
    expect(toScaled(118, 2)).toBe(11800n);
  });

  it('rejects non-finite amounts', () => {
    expect(() => toScaled(Number.NaN, 2)).toThrow(RangeError);
    expect(() => toScaled(Number.POSITIVE_INFINITY, 2)).toThrow(RangeError);
  });

  it('returns the double nearest the scaled decimal', () => {
    expect(fromScaled(2950n, 2)).toBe(29.5);
    expect(fromScaled(10001n, 2)).toBe(100.01);
    expect(fromScaled(1333n, 3)).toBe(1.333);
    expect(normalizeScale(10.555, 2)).toBe(10.56);
    expect(normalizeScale(0.1 + 0.2, 2)).toBe(0.3);
  });

  it('allocates leftover units to the largest remainders, ties by position', () => {
    expect(allocateLargestRemainder(1n, [1000n, 1000n, 1000n])).toEqual([1n, 0n, 0n]);
    expect(allocateLargestRemainder(6n, Array.from({ length: 10 }, () => 1000n))).toEqual([1n, 1n, 1n, 1n, 1n, 1n, 0n, 0n, 0n, 0n]);
    expect(allocateLargestRemainder(1100n, [3333n, 6667n, 1000n])).toEqual([333n, 667n, 100n]);
  });

  it('never allocates to empty weights or beyond a weight', () => {
    expect(allocateLargestRemainder(555n, [0n, 5500n])).toEqual([0n, 555n]);
    expect(allocateLargestRemainder(3n, [1n, 1n, 1n])).toEqual([1n, 1n, 1n]);
    expect(allocateLargestRemainder(5n, [1n, 1n])).toEqual([1n, 1n]);
    expect(allocateLargestRemainder(0n, [10n])).toEqual([0n]);
  });
});
