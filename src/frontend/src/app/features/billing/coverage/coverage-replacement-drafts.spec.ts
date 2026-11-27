import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { stubMemoryStorage } from '../billing.test-utils';
import { ReplacementDrafts, type Intent, type ReplacementScope } from './coverage-replacement.service';

describe('replacement recovery', () => {
  const scope: ReplacementScope = { userId: 'owner', shopId: 'current', account: 'payer', term: 'term', replacement: '', target: '' };
  const intent: Intent = { action: 'schedule', label: 'North → South', body: {
    operationKey: crypto.randomUUID(), outgoingShopId: crypto.randomUUID(), incomingShopId: crypto.randomUUID(),
    observedEffectiveAt: '2028-02-29T04:30:00Z', observedCapacity: 3, observedMemberAllowance: 5,
    observedPermitDeadline: null, acknowledgedRenewal: true, acknowledgedPermitWait: true, acknowledgedShopSelection: true,
  } };
  beforeEach(() => {
    stubMemoryStorage();
    vi.stubGlobal('navigator', { locks: { request: (_: string, run: () => unknown) => Promise.resolve(run()) } });
  });
  afterEach(() => vi.unstubAllGlobals());

  it('recovers both shops and the accepted boundary after reload or a competing tab', async () => {
    await new ReplacementDrafts().freeze(scope, intent);
    const reload = new ReplacementDrafts();
    const key = crypto.randomUUID();
    expect(await reload.freeze(scope, { ...intent, body: { ...intent.body, operationKey: key } })).toEqual(intent);
    await reload.settle(scope, key);
    expect(reload.read(scope)).toEqual(intent);
    await reload.settle(scope, intent.body.operationKey);
    expect(reload.read(scope)).toBeNull();
  });

  it.each(['userId', 'shopId', 'account', 'term', 'replacement', 'target'] as const)(
    'isolates the unresolved request by %s', async field => {
      const storage = new ReplacementDrafts();
      await storage.freeze(scope, intent);
      expect(storage.read({ ...scope, [field]: 'other' })).toBeNull();
    });
});
