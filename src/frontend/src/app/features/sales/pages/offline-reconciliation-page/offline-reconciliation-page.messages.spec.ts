import { describe, expect, it } from 'vitest';

import { issueTypeLabelKey } from './offline-reconciliation-page.messages';

describe('offline reconciliation messages', () => {
  it('labels a document review case', () => {
    expect(issueTypeLabelKey('DocumentReview')).toBe('sales.offlineReconciliation.issueTypes.documentReview');
    expect(issueTypeLabelKey('ClosedPeriodReview')).toBe('sales.offlineReconciliation.issueTypes.closedPeriodReview');
  });

  it('falls back to a generic label for an issue type the console does not know', () => {
    expect(issueTypeLabelKey('SomethingNew')).toBe('sales.offlineReconciliation.issueTypes.unknown');
  });
});
