import { describe, expect, it } from 'vitest';

import { AffiliationDrafts } from './affiliation-drafts';
import { stubMemoryStorage } from '../billing.test-utils';

describe('affiliation drafts', () => {
  it('keeps the exact payload for the same actor and account until the operation settles', () => {
    stubMemoryStorage();
    const first = new AffiliationDrafts();
    const scope = ['payer', 'account', 'request'];
    const { draft: saved } = first.freeze(scope, { shopId: 'shop-a', termId: null, withoutCoverage: true });
    const afterReload = new AffiliationDrafts();
    expect(afterReload.freeze(scope, { shopId: 'shop-b', termId: 'term', withoutCoverage: false }))
      .toEqual({ draft: saved, created: false });
    expect(afterReload.read(['other', 'account', 'request'])).toBeNull();
    expect(afterReload.read(['payer', 'different-account', 'request'])).toBeNull();
    afterReload.settle(scope, saved.operationKey);
    expect(first.read(scope)).toBeNull();
  });

  it('settles only the completed draft when two tabs started separate operations', () => {
    const storage = stubMemoryStorage();
    const scope = ['owner', 'account', 'shop', 'request', 'consent'];
    const drafts = new AffiliationDrafts();
    const { draft: first } = drafts.freeze(scope, { decision: 'grant' });
    const [firstKey, firstEntry] = [...storage.entries()][0];
    storage.set(firstKey.replace(first.operationKey, 'other-operation'),
      JSON.stringify({ operationKey: 'other-operation', decision: 'grant', savedAt: JSON.parse(firstEntry).savedAt + 1 }));
    expect(drafts.read(scope)).toEqual(first);
    drafts.settle(scope, first.operationKey);
    expect(drafts.read(scope)?.operationKey).toBe('other-operation');
    expect(drafts.read(['different-owner', ...scope.slice(1)])).toBeNull();
  });
});
