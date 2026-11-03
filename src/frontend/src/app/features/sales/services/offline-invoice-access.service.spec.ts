import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { OfflineSalesDeviceSettingsStorage } from '../../../core/storage/offline-sales-device-settings.storage';
import type { OfflineQueuedSaleRecord } from './offline-sale-core.types';
import { OfflineInvoiceAccessService } from './offline-invoice-access.service';
import { OfflineSalesQueueIndexedDbService } from './offline-sales-queue-indexeddb.service';

describe('OfflineInvoiceAccessService', () => {
  let session: {
    user: { id: string };
    activeShopId: string | null;
    shops?: { shopId: string; role: string }[];
  } | null;
  const authService = {
    session: vi.fn(() => session),
    isAuthenticated: vi.fn(),
    canUseOfflineSalesAuthGrace: vi.fn(),
  };
  const deviceSettings = { loadSettings: vi.fn() };
  const queueDb = { getQueuedSale: vi.fn() };

  function record(overrides: Partial<OfflineQueuedSaleRecord['payload']> = {}, recordShopId = 'shop-1'): OfflineQueuedSaleRecord {
    return {
      key: 'shop-1::device-1::sale-1',
      shopId: recordShopId,
      deviceId: 'device-1',
      clientSaleId: 'sale-1',
      status: 'Pending',
      payload: { shopId: 'shop-1', deviceId: 'device-1', clientSaleId: 'sale-1', issuedByUserId: 'user-1', ...overrides },
    } as OfflineQueuedSaleRecord;
  }

  function setup(): OfflineInvoiceAccessService {
    TestBed.configureTestingModule({
      providers: [
        OfflineInvoiceAccessService,
        { provide: AuthService, useValue: authService },
        { provide: OfflineSalesDeviceSettingsStorage, useValue: deviceSettings },
        { provide: OfflineSalesQueueIndexedDbService, useValue: queueDb },
      ],
    });
    return TestBed.inject(OfflineInvoiceAccessService);
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
    session = { user: { id: 'user-1' }, activeShopId: 'shop-1' };
    authService.session.mockClear();
    authService.isAuthenticated.mockReset().mockReturnValue(false);
    authService.canUseOfflineSalesAuthGrace.mockReset().mockResolvedValue(true);
    deviceSettings.loadSettings.mockReset().mockReturnValue({ shopId: 'shop-1', deviceId: 'device-1', enabled: true });
    queueDb.getQueuedSale.mockReset().mockResolvedValue(record());
  });

  it('grace_allows_owned_local_print: an expired token inside offline grace reads the issuer\'s own invoice', async () => {
    const service = setup();

    const result = await service.read('sale-1');

    expect(result).toEqual({ granted: true, record: record() });
    expect(queueDb.getQueuedSale).toHaveBeenCalledWith('shop-1', 'device-1', 'sale-1');
    await expect(service.canRead('sale-1')).resolves.toBe(true);
  });

  it('reads without consulting grace while the access token is valid', async () => {
    authService.isAuthenticated.mockReturnValue(true);
    authService.canUseOfflineSalesAuthGrace.mockResolvedValue(false);

    await expect(setup().read('sale-1')).resolves.toMatchObject({ granted: true });
    expect(authService.canUseOfflineSalesAuthGrace).not.toHaveBeenCalled();
  });

  it.each(['Owner', 'Manager'])(
    'lets a signed-in %s reprint a cashier\'s invoice from the shop\'s device queue',
    async (role) => {
      authService.isAuthenticated.mockReturnValue(true);
      session = { user: { id: 'user-1' }, activeShopId: 'shop-1', shops: [{ shopId: 'shop-1', role }] };
      queueDb.getQueuedSale.mockResolvedValue(record({ issuedByUserId: 'user-2' }));

      await expect(setup().read('sale-1')).resolves.toMatchObject({ granted: true });
    },
  );

  it.each([
    ['a signed-in staff member', true, 'Staff'],
    ['a manager working under offline grace', false, 'Manager'],
  ])('keeps another cashier\'s invoice from %s', async (_name, authenticated, role) => {
    authService.isAuthenticated.mockReturnValue(authenticated);
    session = { user: { id: 'user-1' }, activeShopId: 'shop-1', shops: [{ shopId: 'shop-1', role }] };
    queueDb.getQueuedSale.mockResolvedValue(record({ issuedByUserId: 'user-2' }));

    await expect(setup().read('sale-1')).resolves.toEqual({ granted: false, reason: 'NOT_OWNER' });
  });

  it('reads a legacy invoice that recorded no issuer on its own shop and device', async () => {
    queueDb.getQueuedSale.mockResolvedValue(record({ issuedByUserId: undefined }));

    await expect(setup().read('sale-1')).resolves.toMatchObject({ granted: true });
  });

  describe('wrong_scope_denied', () => {
    const cases: [string, () => void, string, boolean][] = [
      ['no session', () => (session = null), 'NO_SESSION', false],
      ['no active shop', () => (session = { user: { id: 'user-1' }, activeShopId: null }), 'NO_SESSION', false],
      ['device never enabled for the shop', () => deviceSettings.loadSettings.mockReturnValue(null), 'NO_DEVICE', false],
      ['offline grace expired', () => authService.canUseOfflineSalesAuthGrace.mockResolvedValue(false), 'GRACE_UNAVAILABLE', false],
      ['missing queue row', () => queueDb.getQueuedSale.mockResolvedValue(null), 'NOT_FOUND', true],
      ['another user issued it', () => queueDb.getQueuedSale.mockResolvedValue(record({ issuedByUserId: 'user-2' })), 'NOT_OWNER', true],
      ['payload belongs to another shop', () => queueDb.getQueuedSale.mockResolvedValue(record({ shopId: 'shop-2' })), 'NOT_FOUND', true],
      ['row belongs to another shop', () => queueDb.getQueuedSale.mockResolvedValue(record({}, 'shop-2')), 'NOT_FOUND', true],
      ['payload from another device', () => queueDb.getQueuedSale.mockResolvedValue(record({ deviceId: 'device-2' })), 'NOT_FOUND', true],
      [
        'session switched shop while the row was read',
        () =>
          queueDb.getQueuedSale.mockImplementation(async () => {
            session = { user: { id: 'user-1' }, activeShopId: 'shop-2' };
            return record();
          }),
        'SCOPE_CHANGED',
        true,
      ],
      [
        'another user signed in while the row was read',
        () =>
          queueDb.getQueuedSale.mockImplementation(async () => {
            session = { user: { id: 'user-2' }, activeShopId: 'shop-1' };
            return record();
          }),
        'SCOPE_CHANGED',
        true,
      ],
    ];

    it.each(cases)('%s', async (_name, arrange, reason, readsQueue) => {
      arrange();
      const service = setup();

      const result = await service.read('sale-1');

      expect(result).toEqual({ granted: false, reason });
      expect('record' in result).toBe(false);
      expect(queueDb.getQueuedSale).toHaveBeenCalledTimes(readsQueue ? 1 : 0);
    });
  });
});
