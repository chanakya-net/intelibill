import { offlineEnablementErrorKeyForReason } from './manage-shop-offline.helper';

describe('offlineEnablementErrorKeyForReason', () => {
  it('explains a lease refused for the GST registration', () => {
    expect(offlineEnablementErrorKeyForReason('LEASE_UNAVAILABLE', 'Invoice.TaxProfileRequired')).toBe(
      'errors.invoice.taxProfileRequired',
    );
    expect(offlineEnablementErrorKeyForReason('LEASE_UNAVAILABLE', 'Invoice.EInvoiceUnsupported')).toBe(
      'errors.invoice.eInvoiceUnsupported',
    );
  });

  it('keeps the generic lease message for any other refusal', () => {
    expect(offlineEnablementErrorKeyForReason('LEASE_UNAVAILABLE', 'Sale.Something')).toBe(
      'offlineSalesDevice.errors.leaseUnavailable',
    );
    expect(offlineEnablementErrorKeyForReason('LEASE_UNAVAILABLE')).toBe('offlineSalesDevice.errors.leaseUnavailable');
    expect(offlineEnablementErrorKeyForReason(undefined)).toBe('offlineSalesDevice.errors.enableFailed');
  });
});
