import { describe, expect, it } from 'vitest';

import { hasProblemCode } from './problem-code.util';

describe('hasProblemCode', () => {
  const code = 'Sale.QuantityPrecisionExceeded';

  it('finds a handler error code in the problem title', () => {
    expect(hasProblemCode({ error: { title: code, detail: 'English text' } }, code)).toBe(true);
  });

  it('finds a validator error code among the problem errors', () => {
    const error = { error: { title: 'Validation.Failed', errors: [{ code: 'Sale.Other' }, { code }] } };

    expect(hasProblemCode(error, code)).toBe(true);
  });

  it('answers false for another code, a malformed body or no body', () => {
    expect(hasProblemCode({ error: { title: 'Sale.Other', errors: [{ code: 'Sale.Other' }] } }, code)).toBe(false);
    expect(hasProblemCode({ error: { errors: 'not a list' } }, code)).toBe(false);
    expect(hasProblemCode({ error: 'text' }, code)).toBe(false);
    expect(hasProblemCode(null, code)).toBe(false);
    expect(hasProblemCode(undefined, code)).toBe(false);
  });
});
