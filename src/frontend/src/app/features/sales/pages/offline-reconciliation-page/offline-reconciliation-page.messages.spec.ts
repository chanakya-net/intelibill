import { describe, expect, it } from 'vitest';

import {
  issueTypeLabelKey,
  licenceHistoryFieldKey,
  licenceHistoryReasonKey,
  licenceHistorySubreasonKey,
  reconciliationErrorKey,
} from './offline-reconciliation-page.messages';

describe('offline reconciliation messages', () => {
  it('labels a document review case', () => {
    expect(issueTypeLabelKey('DocumentReview')).toBe('sales.offlineReconciliation.issueTypes.documentReview');
    expect(issueTypeLabelKey('ClosedPeriodReview')).toBe('sales.offlineReconciliation.issueTypes.closedPeriodReview');
    expect(issueTypeLabelKey('ExpiryReview')).toBe('sales.offlineReconciliation.issueTypes.expiryReview');
    expect(issueTypeLabelKey('LicenseHistoryReview')).toBe(
      'sales.offlineReconciliation.issueTypes.licenseHistoryReview',
    );
  });

  it('falls back to a generic label for an issue type the console does not know', () => {
    expect(issueTypeLabelKey('SomethingNew')).toBe('sales.offlineReconciliation.issueTypes.unknown');
  });

  it('maps licence history reasons, subreasons, and fields with safe unknown fallbacks', () => {
    expect(licenceHistoryReasonKey('OfflineLicenceHistory.EvidenceUnsupported')).toBe(
      'sales.history.offlineQueue.review.reasons.licenceHistoryUnsupported',
    );
    expect(licenceHistoryReasonKey('OfflineLicenceHistory.Supported')).toBe(
      'sales.history.offlineQueue.review.reasons.licenceHistorySupported',
    );
    expect(licenceHistoryReasonKey('Licence.SecuritySuspended')).toBe(
      'sales.offlineReconciliation.errors.licenceSecuritySuspended',
    );
    expect(licenceHistorySubreasonKey('VerificationKeyUnavailable')).toBe(
      'sales.offlineReconciliation.detail.licenceHistory.subreasons.verificationKeyUnavailable',
    );
    expect(licenceHistorySubreasonKey('FutureSubreason')).toBe(
      'sales.offlineReconciliation.detail.licenceHistory.subreasons.unknown',
    );
    expect(licenceHistoryFieldKey('permitId')).toBe(
      'sales.offlineReconciliation.detail.licenceHistory.fields.permitId',
    );
    expect(licenceHistoryFieldKey('unmappedField')).toBe(
      'sales.offlineReconciliation.detail.licenceHistory.fields.unknown',
    );
    expect(reconciliationErrorKey('Licence.SecuritySuspended')).toBe(
      'sales.offlineReconciliation.errors.licenceSecuritySuspended',
    );
  });
});
