import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { stubMemoryStorage } from '../billing.test-utils';
import { CoverageSlotDrafts, type SlotIntent, type SlotScope } from './coverage-slot.service';

describe('unused slot recovery', () => {
  const scope: SlotScope = { userId: 'owner', shopId: 'current', account: 'payer', term: 'term', assignment: '', target: '' };
  const command: SlotIntent = {
    action: 'assign', shopId: crypto.randomUUID(), shopName: 'North', operationKey: crypto.randomUUID(),
    observedCapacity: 3, observedPaidThrough: '2028-02-29T04:30:00Z',
  };
  beforeEach(() => {
    stubMemoryStorage();
    vi.stubGlobal('navigator', { locks: { request: (_: string, run: () => unknown) => Promise.resolve(run()) } });
  });
  afterEach(() => vi.unstubAllGlobals());

  it('keeps the original pending intent and reports a conflict when a different shop or key freezes', async () => {
    const drafts = new CoverageSlotDrafts();
    expect(await drafts.freeze(scope, command)).toEqual({ kind: 'created', intent: command });
    const later: SlotIntent = { ...command, operationKey: crypto.randomUUID(), shopId: crypto.randomUUID(), shopName: 'South' };
    expect(await drafts.freeze(scope, later)).toEqual({ kind: 'existing', intent: command });
    await drafts.settle(scope, later.operationKey);
    expect(drafts.read(scope)).toEqual(command);
    await drafts.settle(scope, command.operationKey);
    expect(drafts.read(scope)).toBeNull();
  });

  it.each(['userId', 'shopId', 'account', 'term', 'target', 'assignment'] as const)(
    'isolates drafts when %s changes', async field => {
      const drafts = new CoverageSlotDrafts();
      await drafts.freeze(scope, command);
      expect(drafts.read({ ...scope, [field]: 'different' })).toBeNull();
    },
  );
});
