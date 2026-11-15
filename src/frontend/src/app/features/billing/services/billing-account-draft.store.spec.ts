import { TestBed } from '@angular/core/testing';

import { stubMemoryStorage } from '../billing.test-utils';
import { BILLING_ACCOUNT_DRAFT_PREFIX, BillingAccountDraftStore } from './billing-account-draft.store';

describe('BillingAccountDraftStore', () => {
  const a = { ownerUserId: 'user-a', operationKey: 'key-a', name: 'Acme' };
  const b = { ownerUserId: 'user-a', operationKey: 'key-b', name: 'Bravo' };
  let store: BillingAccountDraftStore;
  let values: Map<string, string>;

  beforeEach(() => {
    values = stubMemoryStorage();
    TestBed.configureTestingModule({});
    store = TestBed.inject(BillingAccountDraftStore);
  });

  afterEach(() => vi.unstubAllGlobals());

  it('keeps an unfinished attempt for its owner across reloads', () => {
    store.save(a);

    expect(TestBed.inject(BillingAccountDraftStore).load('user-a')).toEqual(a);
    expect([...values.keys()].every((key) => key.startsWith(BILLING_ACCOUNT_DRAFT_PREFIX))).toBe(true);
  });

  it('never hands one person’s attempt to another', () => {
    store.save(a);

    expect(store.load('user-b')).toBeNull();
    store.clear({ ...a, ownerUserId: 'user-b' });
    expect(store.load('user-a')).toEqual(a);
  });

  it('clears only after a definitive answer', () => {
    store.save(a);
    store.clear(a);

    expect(store.load('user-a')).toBeNull();
  });

  it('keeps a second tab’s attempt when the first one settles', () => {
    store.save(a);
    TestBed.inject(BillingAccountDraftStore).save(b);
    store.clear(a);

    expect(store.list('user-a')).toEqual([b]);
  });

  it('settles each attempt by its own key whatever the interleaving', () => {
    store.save(a);
    store.save(b);
    store.clear(b);
    store.clear(b);
    store.save(b);
    store.clear(a);
    store.clear(a);

    expect(store.list('user-a')).toEqual([b]);
  });

  it('lists unfinished attempts oldest first', () => {
    vi.spyOn(Date, 'now').mockReturnValueOnce(2_000).mockReturnValueOnce(1_000);
    store.save(a);
    store.save(b);

    expect(store.list('user-a')).toEqual([b, a]);
    expect(store.load('user-a')).toEqual(b);
  });

  it('ignores malformed and mismatched stored values', () => {
    values.set(`${BILLING_ACCOUNT_DRAFT_PREFIX}user-a:key-x`, '{"operationKey":3}');
    values.set(`${BILLING_ACCOUNT_DRAFT_PREFIX}user-a:key-y`, 'not json');
    values.set(
      `${BILLING_ACCOUNT_DRAFT_PREFIX}user-a:key-z`,
      JSON.stringify({ ownerUserId: 'user-b', operationKey: 'key-z', name: 'Other', savedAt: 1 }),
    );

    expect(store.list('user-a')).toEqual([]);
  });
});
