import { describe, expect, it } from 'vitest';

import { createAuthServiceTestHarness, buildSession } from './auth.service.test-utils';

describe('AuthService (offline auth grace)', () => {
  const t = createAuthServiceTestHarness();

  it('allows offline sales auth grace when requirements are met', async () => {
    t.storage.loadSession.mockReturnValue(
      buildSession({
        activeShopId: 'shop-1',
        shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Staff', isDefault: true, lastUsedAt: null }],
        refreshTokenExpiresAt: new Date(Date.now() + 60_000).toISOString(),
      })
    );
    t.offlineSalesDeviceSettingsStorage.loadSettings.mockReturnValue({
      shopId: 'shop-1',
      deviceId: 'device-1',
      label: 'Counter 1',
      enabled: true,
      enabledAt: '2026-05-21T00:00:00.000Z',
      enabledByUserId: 'user-1',
      enabledByUserName: 'Test User',
      lastCompleteSnapshotAt: '2026-05-21T00:00:00.000Z',
      lastApiVerifiedAt: new Date(Date.now() - 60_000).toISOString(),
      lastSnapshotWarningMarker: null,
      lastReservedLease: null,
    });
    t.offlineSalesSnapshotDb.getUsableSnapshotInfo.mockResolvedValue({
      snapshotId: 'snap-1',
      completedAt: '2026-05-21T00:00:00.000Z',
    });

    const { service } = t.setup();

    await expect(service.canUseOfflineSalesAuthGrace()).resolves.toBe(true);
  });

  it('rejects offline sales auth grace when there is no active shop', async () => {
    t.storage.loadSession.mockReturnValue(buildSession({ activeShopId: null }));
    const { service } = t.setup();
    await expect(service.canUseOfflineSalesAuthGrace()).resolves.toBe(false);
  });

  it('rejects offline sales auth grace when refresh token is expired', async () => {
    t.storage.loadSession.mockReturnValue(
      buildSession({
        activeShopId: 'shop-1',
        refreshTokenExpiresAt: new Date(Date.now() - 60_000).toISOString(),
      })
    );
    const { service } = t.setup();
    await expect(service.canUseOfflineSalesAuthGrace()).resolves.toBe(false);
  });

  it('rejects offline sales auth grace when last API verification is stale', async () => {
    t.storage.loadSession.mockReturnValue(
      buildSession({
        activeShopId: 'shop-1',
        shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Staff', isDefault: true, lastUsedAt: null }],
      })
    );
    t.offlineSalesDeviceSettingsStorage.loadSettings.mockReturnValue({
      shopId: 'shop-1',
      deviceId: 'device-1',
      label: 'Counter 1',
      enabled: true,
      enabledAt: '2026-05-21T00:00:00.000Z',
      enabledByUserId: 'user-1',
      enabledByUserName: 'Test User',
      lastCompleteSnapshotAt: '2026-05-21T00:00:00.000Z',
      lastApiVerifiedAt: new Date(Date.now() - (49 * 60 * 60 * 1000)).toISOString(),
      lastSnapshotWarningMarker: null,
      lastReservedLease: null,
    });
    t.offlineSalesSnapshotDb.getUsableSnapshotInfo.mockResolvedValue({
      snapshotId: 'snap-1',
      completedAt: '2026-05-21T00:00:00.000Z',
    });

    const { service } = t.setup();
    await expect(service.canUseOfflineSalesAuthGrace()).resolves.toBe(false);
  });

  it('rejects offline sales auth grace when offline device is disabled', async () => {
    t.storage.loadSession.mockReturnValue(
      buildSession({
        activeShopId: 'shop-1',
        shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Staff', isDefault: true, lastUsedAt: null }],
      })
    );
    t.offlineSalesDeviceSettingsStorage.loadSettings.mockReturnValue({
      shopId: 'shop-1',
      deviceId: 'device-1',
      label: 'Counter 1',
      enabled: false,
      enabledAt: null,
      enabledByUserId: null,
      enabledByUserName: null,
      lastCompleteSnapshotAt: null,
      lastApiVerifiedAt: null,
      lastSnapshotWarningMarker: null,
      lastReservedLease: null,
    });

    const { service } = t.setup();
    await expect(service.canUseOfflineSalesAuthGrace()).resolves.toBe(false);
  });
});
