import { describe, expect, it } from 'vitest';
import { decidePermitRefresh } from './offline-permit-refresh';

const authorityVerifiedAt = '2026-11-23T00:00:00.000000Z';
const validUntil = '2026-11-23T01:00:00.000000Z';
const held = { authorityVerifiedAt, validUntil, limitingReason: 'AuthorityFreshness', tillSessionId: null };
const shouldReserveForRefresh = (window: typeof held, value: unknown, shopId: string, at: number) =>
  decidePermitRefresh(window, value, shopId, at, { needsLease: false, tillSessionId: null }) === 'reserve';
const preview = { schemaVersion: 1, shopId: 'shop', enforced: true,
  authorityVerifiedAt: '2026-11-23T00:00:00.0000010Z', validUntil: '2026-11-23T01:00:00.0000010Z' };
const now = Date.parse(authorityVerifiedAt) + 1000;

describe('reconnect reservation', () => {
  it('accepts a one-microsecond extension to a fixed commercial end', () => {
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
  it('keeps the range for later authority with the same horizon', () => {
    expect(shouldReserveForRefresh(held, { ...preview, validUntil }, 'shop', now)).toBe(false);
  });
  it('requires a day of gain for a sliding 48-hour horizon', () => {
    const start = Date.parse(authorityVerifiedAt);
    const day = 86_400_000;
    const window = { ...held, validUntil: new Date(start + 2 * day).toISOString() };
    for (const [age, expected] of [[day - 1, false], [day, true]] as const) {
      const next = { ...preview, authorityVerifiedAt: new Date(start + age).toISOString(),
        validUntil: new Date(start + age + 2 * day).toISOString() };
      expect(shouldReserveForRefresh(window, next, 'shop', start + age)).toBe(expected);
    }
  });
  it('refreshes immediately when renewal removes the held commercial cap', () => {
    const at = Date.parse(authorityVerifiedAt) + 60_000;
    const day = 86_400_000;
    const signed = { ...held, limitingReason: 'CurrentLicenceEnd', validUntil: new Date(at + 1.5 * day).toISOString() };
    const next = { ...preview, authorityVerifiedAt: new Date(at).toISOString(), validUntil: new Date(at + 2 * day).toISOString() };
    expect(shouldReserveForRefresh(signed, next, 'shop', at)).toBe(true);
  });
  it.each(['AuthDeadline', 'TillDayEnd', 'FiscalYearEnd'])('honours %s before requesting a range', reason => {
    const boundary = reason === 'FiscalYearEnd' ? '2027-03-31T18:30:00Z' : validUntil;
    const at = Date.parse(boundary) - 1_000;
    const signed = { authorityVerifiedAt: new Date(at - 60_000).toISOString(), validUntil: boundary,
      limitingReason: reason, tillSessionId: 'till' };
    const next = { ...preview, authorityVerifiedAt: new Date(at).toISOString(), validUntil: new Date(at + 3_600_000).toISOString() };
    expect(decidePermitRefresh(signed, next, 'shop', at, { needsLease: false, tillSessionId: 'till',
      authDeadline: reason === 'AuthDeadline' ? boundary : undefined })).toBe('healthy');
  });
  it('refuses a replacement after the same till day ends', () => {
    const signed = { ...held, limitingReason: 'TillDayEnd', tillSessionId: 'till' };
    expect(decidePermitRefresh(signed, preview, 'shop', Date.parse(validUntil), {
      needsLease: true, tillSessionId: 'till',
    })).toBe('unverified');
  });
  it('rejects a preview at its exclusive end', () => {
    expect(shouldReserveForRefresh(held, { ...preview, validUntil }, 'shop', Date.parse(validUntil))).toBe(false);
  });
});
