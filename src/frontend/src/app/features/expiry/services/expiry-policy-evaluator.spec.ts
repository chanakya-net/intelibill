import { describe, expect, it } from 'vitest';
import contract from '../../../../../../../tests/contracts/expiry-policy-v1.json';
import {
  evaluateExpiry,
  shopBusinessDate,
  millisecondsUntilShopDateChange,
} from './expiry-policy-evaluator';
import type { ExpiryPolicy } from './expiry.models';

describe('expiry policy /1 shared server contract', () => {
  for (const entry of contract.cases) {
    it(entry.name, () => {
      expect(
        evaluateExpiry(
          entry.batch,
          entry.policy as ExpiryPolicy,
          entry.catalogVersion,
          entry.businessDate,
        ),
      ).toMatchObject({
        ...entry.expected,
        businessDate: entry.businessDate,
        policyVersion: entry.policy.version,
        catalogVersion: entry.catalogVersion,
        physicalQuantity: entry.batch.physicalQuantity,
        quarantinedQuantity: entry.batch.quarantinedQuantity,
      });
    });
  }
  it('expires cached availability at the actual shop midnight, including a DST boundary', () => {
    expect(
      millisecondsUntilShopDateChange(Date.parse('2026-11-08T18:29:59Z'), 'Asia/Kolkata'),
    ).toBe(1000);
    expect(
      millisecondsUntilShopDateChange(Date.parse('2026-11-01T04:00:00Z'), 'America/New_York'),
    ).toBe(25 * 60 * 60 * 1000);
  });
  it('uses the supplied shop zone across midnight rather than UTC or browser date', () => {
    expect(shopBusinessDate('2026-11-08T18:29:59Z', 'Asia/Kolkata')).toBe('2026-11-08');
    expect(shopBusinessDate('2026-11-08T18:30:00Z', 'Asia/Kolkata')).toBe('2026-11-09');
    expect(shopBusinessDate('2026-11-08T01:00:00Z', 'America/Los_Angeles')).toBe('2026-11-07');
    expect(() => shopBusinessDate('2026-11-08T18:30:00Z', '')).toThrow();
  });
});
