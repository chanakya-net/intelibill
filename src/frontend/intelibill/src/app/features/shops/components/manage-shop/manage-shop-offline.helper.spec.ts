import {
  formatOfflinePackVersion,
  getOfflinePermitDisplayState,
  offlineEnablementErrorKeyForReason,
  offlineEnablementProgressKey,
} from './manage-shop-offline.helper';

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

describe('getOfflinePermitDisplayState', () => {
  const baseSettings = {
    shopId: 'shop-1',
    deviceId: 'device-1',
    label: 'Counter 1',
    enabled: true,
    enabledAt: '2026-11-20T12:00:00Z',
    enabledByUserId: 'user-1',
    enabledByUserName: 'Owner',
    lastCompleteSnapshotAt: '2026-11-20T12:00:00Z',
    lastApiVerifiedAt: '2026-11-20T12:00:00Z',
    lastSnapshotWarningMarker: null,
    lastReservedLease: {
      leaseId: 'lease-1',
      fiscalYear: '2026-27',
      remainingCount: 200,
      expiresAt: '2026-11-27T12:00:00Z',
      licenceGrantId: 'grant-1',
    },
    lastOfflinePermit: {
      leaseId: 'lease-1',
      grantId: 'grant-1',
      permitId: 'permit-1',
      snapshotId: 'snap-1',
      issuedAt: '2026-11-22T11:59:30.123456Z',
      notBefore: '2026-11-22T11:59:30.123456Z',
      validUntil: '2026-11-22T12:00:00.000000Z',
      limitingReason: 'AuthorityFreshness',
    },
  };

  it('returns deadline when permit matches the current anchored lease', () => {
    expect(getOfflinePermitDisplayState(baseSettings)).toEqual({
      kind: 'deadline',
      validUntil: '2026-11-22T12:00:00.000000Z',
    });
  });

  it('returns notReceived when lease is anchored but has no matching permit', () => {
    expect(getOfflinePermitDisplayState({ ...baseSettings, lastOfflinePermit: null })).toEqual({ kind: 'notReceived' });
    expect(
      getOfflinePermitDisplayState({
        ...baseSettings,
        lastOfflinePermit: { ...baseSettings.lastOfflinePermit, leaseId: 'other-lease' },
      }),
    ).toEqual({ kind: 'notReceived' });
  });

  it('returns none for legacy leases and disabled devices', () => {
    expect(
      getOfflinePermitDisplayState({
        ...baseSettings,
        lastReservedLease: { leaseId: 'lease-1', fiscalYear: '2026-27', remainingCount: 200, expiresAt: '2026-11-27T12:00:00Z' },
        lastOfflinePermit: null,
      }),
    ).toEqual({ kind: 'none' });
    expect(getOfflinePermitDisplayState({ ...baseSettings, enabled: false })).toEqual({ kind: 'none' });
    expect(getOfflinePermitDisplayState(null)).toEqual({ kind: 'none' });
  });
});
