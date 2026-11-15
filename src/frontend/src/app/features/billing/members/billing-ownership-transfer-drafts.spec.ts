import { stubMemoryStorage } from '../billing.test-utils';
import { BillingOwnershipTransferDrafts } from './billing-ownership-transfer-drafts';
import { OWNERSHIP_POLICY, type OwnershipDraft } from './billing-ownership-transfer.service';

describe('BillingOwnershipTransferDrafts', () => {
  it('retains the frozen acceptance after a lost response and reload', () => {
    stubMemoryStorage();
    const first: OwnershipDraft = { action: 'accept', target: 'proposal', accountId: 'account', body: {
      operationKey: 'a', expectedVersion: 1, responsibilityPolicyVersion: OWNERSHIP_POLICY, acceptResponsibility: true,
    } };
    const second: OwnershipDraft = { ...first, body: { ...first.body, operationKey: 'b' } };
    new BillingOwnershipTransferDrafts().save('actor', first);
    new BillingOwnershipTransferDrafts().save('actor', second);
    const reloaded = new BillingOwnershipTransferDrafts();
    expect(reloaded.list('actor')).toEqual([first, second]);
    expect(reloaded.list('stranger')).toEqual([]);
    reloaded.settle('actor', first);
    expect(reloaded.list('actor')).toEqual([second]);
  });
});
