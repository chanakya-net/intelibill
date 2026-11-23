import { describe, expect, it } from 'vitest';
import { shouldReserveForRefresh } from './offline-permit-refresh';

const authorityVerifiedAt = '2026-11-23T00:00:00.000000Z';
const validUntil = '2026-11-23T01:00:00.000000Z';
const held = { authorityVerifiedAt, validUntil };
const preview = { schemaVersion: 1, shopId: 'shop', enforced: true,
  authorityVerifiedAt: '2026-11-23T00:00:00.0000010Z', validUntil: '2026-11-23T01:00:00.0000010Z' };
const now = Date.parse(authorityVerifiedAt) + 1000;

describe('reconnect reservation', () => {
  it('accepts a one-microsecond gain', () => {
    expect(shouldReserveForRefresh(held, preview, 'shop', now)).toBe(true);
  });
  it.each([{ authorityVerifiedAt }, { shopId: 'other' }, { schemaVersion: 2 }, { validUntil: 'invalid' }])(
    'rejects stale or unbound previews %j', overrides => {
      expect(shouldReserveForRefresh(held, { ...preview, ...overrides }, 'shop', now)).toBe(false);
    });
  it('keeps a usable range when the advertised window would end earlier', () => {
    const shorter = { ...preview, validUntil: '2026-11-23T00:30:00.000000Z' };
    expect(shouldReserveForRefresh(held, shorter, 'shop', now)).toBe(false);
  });
  it('permits fresh authority with the same horizon for a capacity change', () => {
    expect(shouldReserveForRefresh(held, { ...preview, validUntil }, 'shop', now)).toBe(true);
  });
  it('rejects a preview at its exclusive end', () => {
    expect(shouldReserveForRefresh(held, { ...preview, validUntil }, 'shop', Date.parse(validUntil))).toBe(false);
  });
});
