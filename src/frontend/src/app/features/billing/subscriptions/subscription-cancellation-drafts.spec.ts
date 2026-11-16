import { stubMemoryStorage } from '../billing.test-utils';
import { SubscriptionCancellationDrafts } from './subscription-cancellation-drafts';
import type { CancellationCommand } from './subscription-cancellation.service';

const scope = { actor: 'actor', account: 'account', term: 'term' };
const command: CancellationCommand = {
  operationKey: 'key', expectedVersion: 0, sourceHash: 'a'.repeat(64),
  acceptedEffectiveAt: '2027-02-28T04:30:00Z', affectedShopSetHash: 'b'.repeat(64),
  affectedShopCount: 2, policyVersion: 'term-boundary/1', confirmed: true,
};

describe('SubscriptionCancellationDrafts', () => {
  it('retains the first accepted payload across concurrent preparation and reload', async () => {
    stubMemoryStorage();
    let queue: Promise<unknown> = Promise.resolve();
    vi.stubGlobal('navigator', { locks: { request: (_: string, operation: () => unknown) => {
      const result = queue.then(operation);
      queue = result.catch(() => undefined);
      return result;
    } } });
    const first = new SubscriptionCancellationDrafts();
    const reloaded = new SubscriptionCancellationDrafts();
    const different = { ...command, operationKey: 'different' };
    expect(await Promise.all([first.prepare(scope, command), reloaded.prepare(scope, different)]))
      .toEqual([{ command, settled: false }, { command, settled: false }]);
    expect(reloaded.read({ ...scope, actor: 'other' })).toBeNull();
    expect(reloaded.read({ ...scope, account: 'other' })).toBeNull();
    expect(reloaded.read({ ...scope, term: 'other' })).toBeNull();
    await first.settle(scope, different.operationKey);
    expect(reloaded.read(scope)?.settled).toBe(false);
    await first.settle(scope, command.operationKey);
    expect(await reloaded.prepare(scope, different)).toEqual({ command, settled: true });
  });
});
