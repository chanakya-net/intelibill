import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import type { AuthSession } from '../../../core/auth/auth.models';
import { NetworkStatusService } from '../../../core/services/network-status.service';
import { OfflineSalesDeviceSettingsStorage } from '../../../core/storage/offline-sales-device-settings.storage';
import { InvoiceLeaseIndexedDbService } from '../../../core/storage/invoice-lease-indexeddb.service';
import { OfflineSalesSnapshotIndexedDbService } from '../../../core/storage/offline-sales-snapshot-indexeddb.service';
import { OfflineSalesSnapshotSyncService } from './offline-sales-snapshot-sync.service';
import { OfflineSalesDeviceEnablementService } from './offline-sales-device-enablement.service';
import { SaleService } from './sale.service';

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

  it('persists enabled settings only after snapshot and lease succeed', async () => {
    const service = setup();

    const result = await service.enableForShop('shop-1', 'Counter 1');

    expect(result.ok).toBe(true);
    expect(leaseDb.saveLease).toHaveBeenCalledTimes(1);
    expect(settingsStorage.updateSettings).toHaveBeenCalledTimes(1);
  });
});

