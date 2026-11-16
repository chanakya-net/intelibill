import { stubMemoryStorage } from '../billing.test-utils';
import { SubscriptionCancellationWithdrawalDrafts } from './subscription-cancellation-withdrawal-drafts';
import type { WithdrawalCommand } from './subscription-cancellation.service';

const scope = { actor: 'actor', account: 'account', term: 'term' };
const command: WithdrawalCommand = {
  operationKey: 'key', currentVersion: 1, changeId: 'change', acceptedEffectiveAt: '2027-02-28T04:30:00Z', confirmed: true,
};

describe('SubscriptionCancellationWithdrawalDrafts', () => {
  beforeEach(() => {
    stubMemoryStorage();
    vi.stubGlobal('navigator', { locks: { request: async (_: string, run: () => unknown) => run() } });
  });
  afterEach(() => vi.unstubAllGlobals());

  it('freezes one withdrawal per actor/account/term until a newer schedule supersedes its settled record', async () => {
    const drafts = new SubscriptionCancellationWithdrawalDrafts();
    const other = { ...command, operationKey: 'other' };
    expect(await drafts.prepare(scope, command)).toEqual({ command, settled: false });
    expect(await drafts.prepare(scope, other)).toEqual({ command, settled: false });
    expect(drafts.read({ ...scope, actor: 'someone' })).toBeNull();
    await drafts.settle(scope, 'other');
    expect(drafts.read(scope)?.settled).toBe(false);
    await drafts.settle(scope, 'key');
    expect(await drafts.prepare(scope, other)).toEqual({ command, settled: true });
    const later = { ...command, operationKey: 'later', currentVersion: 3, changeId: 'next' };
    expect(await drafts.prepare(scope, later)).toEqual({ command: later, settled: false });
  });

  it('discards only an unsettled rejected command and refuses malformed records', async () => {
    const drafts = new SubscriptionCancellationWithdrawalDrafts();
    await drafts.prepare(scope, command);
    await drafts.discardRejected(scope, 'other');
    expect(drafts.read(scope)?.command).toEqual(command);
    await drafts.discardRejected(scope, 'key');
    expect(drafts.read(scope)).toBeNull();
    localStorage.setItem('intelibill.billing.cancellation-withdrawal.v1:' + JSON.stringify(['actor', 'account', 'term']),
      JSON.stringify({ command: { ...command, currentVersion: 2 }, settled: false }));
    expect(() => drafts.read(scope)).toThrow();
  });
});
