import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom, of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import type { AuthSession } from '../../../core/auth/auth.models';
import { NetworkStatusService } from '../../../core/services/network-status.service';
import { OfflineSalesDeviceSettingsStorage } from '../../../core/storage/offline-sales-device-settings.storage';
import { InvoiceLeaseIndexedDbService } from '../../../core/storage/invoice-lease-indexeddb.service';
import { OfflineSalesSnapshotIndexedDbService } from '../../../core/storage/offline-sales-snapshot-indexeddb.service';
import { OfflineSalesQueueSyncService } from './offline-sales-queue-sync.service';
import { OfflineSalesSnapshotSyncService } from './offline-sales-snapshot-sync.service';
import { OfflineSalesDeviceEnablementService } from './offline-sales-device-enablement.service';
import { SaleService } from './sale.service';
import { OfflinePackService } from '../../../core/service-worker/offline-pack.service';

describe('OfflineSalesDeviceEnablementService', () => {
  const session: AuthSession = {
    accessToken: 'token',
    refreshToken: 'refresh',
    accessTokenExpiresAt: '2099-01-01T00:00:00.000Z',
    refreshTokenExpiresAt: '2099-01-01T00:00:00.000Z',
    rememberMe: true,
    user: {
      id: 'user-1',
      email: 'a@b.com',
      phoneNumber: null,
      firstName: 'Test',
      lastName: 'User',
      language: 'en-IN',
    },
    activeShopId: 'shop-1',
    shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null }],
  };

  const auth = { session: signal<AuthSession | null>(session) } satisfies Partial<AuthService>;

  const canReachApi = signal(true);
  const lastVerifiedAt = signal<Date | null>(new Date('2026-05-21T00:00:00.000Z'));
  const network = {
    canReachApi,
    lastVerifiedAt,
    checkConnectivity: vi.fn(async () => {}),
  } satisfies Partial<NetworkStatusService>;

  const settingsStorage = {
    getOrCreateDeviceId: vi.fn((_shopId: string) => 'device-1'),
    updateSettings: vi.fn((_shopId: string, update: (current: any) => any) => update({
      shopId: 'shop-1',
      deviceId: 'device-1',
      label: '',
      enabled: false,
      enabledAt: null,
      enabledByUserId: null,
      enabledByUserName: null,
      lastCompleteSnapshotAt: null,
      lastApiVerifiedAt: null,
      lastSnapshotWarningMarker: null,
      lastReservedLease: null,
    })),
  } satisfies Partial<OfflineSalesDeviceSettingsStorage>;

  const snapshotSync = { syncForShop: vi.fn(async (_shopId: string) => {}) } satisfies Partial<OfflineSalesSnapshotSyncService>;
  const snapshotDb = {
    getUsableSnapshotInfo: vi.fn(async (_shopId: string) => ({ snapshotId: 'snap-1', completedAt: '2026-05-21T00:00:00.000Z' })),
  } satisfies Partial<OfflineSalesSnapshotIndexedDbService>;

  const saleService = {
    reserveInvoiceLease: vi.fn(() => of({
      leaseId: 'lease-1',
      shopId: 'shop-1',
      deviceId: 'device-1',
      fiscalYear: '2026-2027',
      prefix: 'INV-',
      numberPadding: 6,
      rangeStart: 1,
      rangeEnd: 100,
      nextNumber: 1,
      remainingCount: 100,
      reservedAt: '2026-05-21T00:00:00.000Z',
      expiresAt: '2026-06-21T00:00:00.000Z',
    })),
  } satisfies Partial<SaleService>;

  const leaseDb = { saveLease: vi.fn(async (_lease: any) => {}) } satisfies Partial<InvoiceLeaseIndexedDbService>;
  const queueSync = { syncForShop: vi.fn(async (_shopId: string, _deviceId: string) => ({
    attemptedCount: 0,
    syncedCount: 0,
    warningCount: 0,
    needsReviewCount: 0,
    failedCount: 0,
  })) } satisfies Partial<OfflineSalesQueueSyncService>;

  function setup(): OfflineSalesDeviceEnablementService {
    TestBed.configureTestingModule({
      providers: [
        OfflineSalesDeviceEnablementService,
        { provide: AuthService, useValue: auth },
        { provide: NetworkStatusService, useValue: network },
        { provide: OfflineSalesDeviceSettingsStorage, useValue: settingsStorage },
        { provide: OfflineSalesSnapshotSyncService, useValue: snapshotSync },
        { provide: OfflineSalesSnapshotIndexedDbService, useValue: snapshotDb },
        { provide: SaleService, useValue: saleService },
        { provide: InvoiceLeaseIndexedDbService, useValue: leaseDb },
        { provide: OfflineSalesQueueSyncService, useValue: queueSync },
      ],
    });

    return TestBed.inject(OfflineSalesDeviceEnablementService);
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
    canReachApi.set(true);
    (network.checkConnectivity as ReturnType<typeof vi.fn>).mockClear();
    (snapshotSync.syncForShop as ReturnType<typeof vi.fn>).mockClear();
    (snapshotDb.getUsableSnapshotInfo as ReturnType<typeof vi.fn>).mockClear();
    (saleService.reserveInvoiceLease as ReturnType<typeof vi.fn>).mockClear();
    (leaseDb.saveLease as ReturnType<typeof vi.fn>).mockClear();
    (queueSync.syncForShop as ReturnType<typeof vi.fn>).mockClear();
    (settingsStorage.updateSettings as ReturnType<typeof vi.fn>).mockClear();
  });

  it('blocks enablement when API is unreachable', async () => {
    const service = setup();
    canReachApi.set(false);

    const result = await service.enableForShop('shop-1', 'Counter 1');

    expect(result.ok).toBe(false);
    expect(result).toEqual({ ok: false, reason: 'API_UNREACHABLE' });
    expect(snapshotSync.syncForShop).not.toHaveBeenCalled();
    expect(saleService.reserveInvoiceLease).not.toHaveBeenCalled();
    expect(settingsStorage.updateSettings).not.toHaveBeenCalled();
  });

  it('blocks enablement when selected shop differs from active shop', async () => {
    const service = setup();

    const result = await service.enableForShop('shop-2', 'Counter 2');

    expect(result.ok).toBe(false);
    expect(result).toEqual({ ok: false, reason: 'ACTIVE_SHOP_MISMATCH' });
    expect(network.checkConnectivity).not.toHaveBeenCalled();
    expect(snapshotSync.syncForShop).not.toHaveBeenCalled();
    expect(saleService.reserveInvoiceLease).not.toHaveBeenCalled();
    expect(settingsStorage.updateSettings).not.toHaveBeenCalled();
  });

  it('does not persist enabled state when snapshot is incomplete', async () => {
    const service = setup();
    (snapshotDb.getUsableSnapshotInfo as ReturnType<typeof vi.fn>).mockResolvedValueOnce(null);

    const result = await service.enableForShop('shop-1', 'Counter 1');

    expect(result.ok).toBe(false);
    expect(result).toEqual({ ok: false, reason: 'SNAPSHOT_INCOMPLETE' });
    expect(saleService.reserveInvoiceLease).not.toHaveBeenCalled();
    expect(leaseDb.saveLease).not.toHaveBeenCalled();
    expect(settingsStorage.updateSettings).not.toHaveBeenCalled();
  });

  it('does not persist enabled state when lease reservation fails', async () => {
    const service = setup();
    (saleService.reserveInvoiceLease as ReturnType<typeof vi.fn>).mockReturnValueOnce(throwError(() => new Error('fail')));

    const result = await service.enableForShop('shop-1', 'Counter 1');

    expect(result.ok).toBe(false);
    expect(result).toEqual({ ok: false, reason: 'LEASE_UNAVAILABLE' });
    expect(leaseDb.saveLease).not.toHaveBeenCalled();
    expect(settingsStorage.updateSettings).not.toHaveBeenCalled();
  });

  it('passes on why the server refused the lease', async () => {
    const service = setup();
    (saleService.reserveInvoiceLease as ReturnType<typeof vi.fn>).mockReturnValueOnce(
      throwError(() => ({ status: 400, error: { title: 'Invoice.TaxProfileRequired', detail: 'Declare it.' } })),
    );

    const result = await service.enableForShop('shop-1', 'Counter 1');

    expect(result).toEqual({ ok: false, reason: 'LEASE_UNAVAILABLE', errorCode: 'Invoice.TaxProfileRequired' });
    expect(settingsStorage.updateSettings).not.toHaveBeenCalled();
  });

  it('persists enabled settings only after snapshot and lease succeed', async () => {
    const service = setup();

    const result = await service.enableForShop('shop-1', 'Counter 1');

    expect(result.ok).toBe(true);
    expect(leaseDb.saveLease).toHaveBeenCalledTimes(1);
    expect(settingsStorage.updateSettings).toHaveBeenCalledTimes(1);
  });

  it('keeps the seller profile the server froze on the lease', async () => {
    const sellerProfile = {
      seller: {
        legalName: 'Main Shop Pvt Ltd',
        tradeName: 'Main Shop',
        address: '42 MG Road, Bengaluru, Karnataka 560001',
        stateCode: '29',
        stateName: 'Karnataka',
        gstin: '29AAPFU0939F1ZR',
        taxRegime: 'Regular' as const,
      },
      eInvoiceStatus: 'BelowThreshold' as const,
      seriesCode: 'A',
    };
    const reserved = await firstValueFrom(saleService.reserveInvoiceLease());
    (saleService.reserveInvoiceLease as ReturnType<typeof vi.fn>).mockReturnValueOnce(of({ ...reserved, sellerProfile }));
    const service = setup();

    const result = await service.enableForShop('shop-1', 'Counter 1');

    expect(result.ok).toBe(true);
    expect(leaseDb.saveLease.mock.calls[0][0].sellerProfile).toEqual(sellerProfile);
  });

  it('triggers queue sync after successful enablement', async () => {
    const service = setup();

    const result = await service.enableForShop('shop-1', 'Counter 1');

    expect(result.ok).toBe(true);
    expect(queueSync.syncForShop).toHaveBeenCalledWith('shop-1', 'device-1');
  });

  describe('offline pack step', () => {
    const pack = {
      prepare: vi.fn(),
      release: vi.fn(async () => undefined),
    };

    function setupWithPack(): OfflineSalesDeviceEnablementService {
      TestBed.overrideProvider(OfflinePackService, { useValue: pack });
      return setup();
    }

    beforeEach(() => {
      pack.prepare.mockReset();
      pack.release.mockClear();
    });

    it('downloads and verifies the app files before the snapshot, and keeps the flag when all steps pass', async () => {
      const steps: (string | null)[] = [];
      pack.prepare.mockImplementation(async () => ({ ok: true, skipped: false, status: {} }));
      const service = setupWithPack();
      snapshotSync.syncForShop.mockImplementationOnce(async () => {
        steps.push(service.step());
      });
      pack.prepare.mockImplementationOnce(async () => {
        steps.push(service.step());
        return { ok: true, skipped: false, status: {} };
      });

      const result = await service.enableForShop('shop-1', 'Counter 1');

      expect(result.ok).toBe(true);
      expect(steps).toEqual(['appPack', 'snapshot']);
      expect(pack.prepare.mock.invocationCallOrder[0]).toBeLessThan(snapshotSync.syncForShop.mock.invocationCallOrder[0]);
      expect(pack.release).not.toHaveBeenCalled();
      expect(service.step()).toBeNull();
    });

    it('fails with APP_PACK_INCOMPLETE before any snapshot or lease, and lets the flag go', async () => {
      pack.prepare.mockResolvedValue({ ok: false, failure: 'TIMEOUT' });
      const service = setupWithPack();

      const result = await service.enableForShop('shop-1', 'Counter 1');

      expect(result).toEqual({ ok: false, reason: 'APP_PACK_INCOMPLETE', packFailure: 'TIMEOUT' });
      expect(snapshotSync.syncForShop).not.toHaveBeenCalled();
      expect(saleService.reserveInvoiceLease).not.toHaveBeenCalled();
      expect(settingsStorage.updateSettings).not.toHaveBeenCalled();
      expect(pack.release).toHaveBeenCalledTimes(1);
    });

    it('lets the flag go when a later step fails', async () => {
      pack.prepare.mockResolvedValue({ ok: true, skipped: false, status: {} });
      (snapshotDb.getUsableSnapshotInfo as ReturnType<typeof vi.fn>).mockResolvedValueOnce(null);
      const service = setupWithPack();

      const result = await service.enableForShop('shop-1', 'Counter 1');

      expect(result).toEqual({ ok: false, reason: 'SNAPSHOT_INCOMPLETE' });
      expect(pack.release).toHaveBeenCalledTimes(1);
    });

    it('never touches the pack when enabling stops before it (unreachable API)', async () => {
      const service = setupWithPack();
      canReachApi.set(false);

      await service.enableForShop('shop-1', 'Counter 1');

      expect(pack.prepare).not.toHaveBeenCalled();
      expect(pack.release).not.toHaveBeenCalled();
    });
  });
});
