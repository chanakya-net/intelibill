import { formatOfflinePackVersion, offlineEnablementErrorKeyForReason, offlineEnablementProgressKey } from './manage-shop-offline.helper';

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

describe('offline pack messages', () => {
  it('says why the app files are not on the device', () => {
    expect(offlineEnablementErrorKeyForReason('APP_PACK_INCOMPLETE', undefined, 'STORAGE_LOW')).toBe(
      'offlineSalesDevice.errors.appPackStorageLow',
    );
    for (const failure of ['TIMEOUT', 'INSTALL_FAILED', 'UNREACHABLE', undefined]) {
      expect(offlineEnablementErrorKeyForReason('APP_PACK_INCOMPLETE', undefined, failure)).toBe(
        'offlineSalesDevice.errors.appPackIncomplete',
      );
    }
  });

  it('names the step an enable is on', () => {
    expect(offlineEnablementProgressKey('appPack')).toBe('offlineSalesDevice.progress.appPack');
    expect(offlineEnablementProgressKey('snapshot')).toBe('offlineSalesDevice.progress.snapshot');
    expect(offlineEnablementProgressKey('lease')).toBe('offlineSalesDevice.progress.lease');
    expect(offlineEnablementProgressKey(null)).toBe('offlineSalesDevice.enabling');
  });

  it('shortens the build id for display', () => {
    expect(
      formatOfflinePackVersion({
        state: 'ready',
        buildId: '0123456789abcdef',
        manifestHash: null,
        persisted: null,
        lastCheckedAt: null,
        lastUpdatedAt: null,
      }),
    ).toBe('01234567');
    expect(formatOfflinePackVersion(null)).toBe('');
  });
});
