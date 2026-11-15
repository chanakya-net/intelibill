import { TestBed } from '@angular/core/testing';

import { stubMemoryStorage } from '../billing.test-utils';
import { BILLING_ACCOUNT_DRAFT_KEY, BillingAccountDraftStore } from './billing-account-draft.store';

describe('BillingAccountDraftStore', () => {
  let store: BillingAccountDraftStore;

  beforeEach(() => {
    stubMemoryStorage();
    TestBed.configureTestingModule({});
    store = TestBed.inject(BillingAccountDraftStore);
  });

  afterEach(() => vi.unstubAllGlobals());

  it('keeps an unfinished attempt for its owner across reloads', () => {
    store.save({ ownerUserId: 'user-a', operationKey: 'key-1', name: 'Acme' });

    expect(TestBed.inject(BillingAccountDraftStore).load('user-a')).toEqual({
      ownerUserId: 'user-a',
      operationKey: 'key-1',
      name: 'Acme',
    });
    expect(localStorage.getItem(BILLING_ACCOUNT_DRAFT_KEY)).not.toBeNull();
  });

  it('never hands one person’s attempt to another', () => {
    store.save({ ownerUserId: 'user-a', operationKey: 'key-1', name: 'Acme' });

    expect(store.load('user-b')).toBeNull();
    store.clear('user-b');
    expect(store.load('user-a')).not.toBeNull();
  });

  it('clears only after a definitive answer', () => {
    store.save({ ownerUserId: 'user-a', operationKey: 'key-1', name: 'Acme' });
    store.clear('user-a');

    expect(store.load('user-a')).toBeNull();
  });

  it('ignores malformed stored values', () => {
    localStorage.setItem(BILLING_ACCOUNT_DRAFT_KEY, '{"user-a":{"operationKey":3}}');

    expect(store.load('user-a')).toBeNull();
  });
});
