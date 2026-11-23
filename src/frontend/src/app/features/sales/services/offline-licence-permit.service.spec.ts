import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import type { AuthSession } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import { InvoiceLeaseIndexedDbService, type InvoiceLeaseSnapshot } from '../../../core/storage/invoice-lease-indexeddb.service';
import {
  OfflineSalesDeviceSettingsStorage,
  type OfflineSalesDeviceSettings,
} from '../../../core/storage/offline-sales-device-settings.storage';
import { TillContextService } from '../../tills/services/till-context.service';
import { OfflinePermitClockIndexedDbService } from '../../../core/storage/offline-permit-clock-indexeddb.service';
import { OFFLINE_PERMIT_PUBLIC_KEYS } from '../utils/offline-permit-signature';
import { OfflineLicencePermitService } from './offline-licence-permit.service';

const PERMIT_FIXTURE_PATH = resolve(process.cwd(), '../../tests/contracts/licensing/offline-permit-v1.json');
const DELIVERY_FIXTURE_PATH = resolve(process.cwd(), '../../tests/contracts/licensing/offline-permit-delivery-v1.json');

const permitFixture = JSON.parse(readFileSync(PERMIT_FIXTURE_PATH, 'utf8')) as {
  key: { keyId: string; publicKeySpki: string };
  permit: { token: string; fields: Record<string, string> };
};
const deliveryFixture = JSON.parse(readFileSync(DELIVERY_FIXTURE_PATH, 'utf8')) as {
  leaseEvidence: { version: number; grantId: string; grantSha256: string; keyId: string };
  issuedRecord: { licencePermit: Record<string, unknown> };
  refusedRecord: { licencePermit: Record<string, unknown> };
};

describe('OfflineLicencePermitService', () => {
  const fields = permitFixture.permit.fields;
  const shopId = fields['shopId'];
  const userId = fields['userId'];
  const sessionId = fields['sessionId'];
  const deviceId = fields['deviceId'];
  const leaseId = fields['leaseId'];

  const sessionSignal = signal<AuthSession | null>(null);
  const tillEnabled = signal(false);
  const tillSelected = signal<{ readonly id: string } | null>(null);
  let storedSettings: OfflineSalesDeviceSettings | null;
  let storedLease: InvoiceLeaseSnapshot | null;

  const settingsStorage = {
    loadSettings: vi.fn((_shopId: string) => storedSettings),
    updateSettings: vi.fn((_shopId: string, updater: (current: OfflineSalesDeviceSettings) => OfflineSalesDeviceSettings) => {
      if (!storedSettings) return null;
      storedSettings = updater(storedSettings);
      return storedSettings;
    }),
  } satisfies Partial<OfflineSalesDeviceSettingsStorage>;

  const leaseDb = {
    loadLease: vi.fn(async () => storedLease),
    attachPermitUnderLock: vi.fn(async (_shop: string, _device: string, _fy: string, permit: any) => {
      if (!storedLease || storedLease.leaseId !== permit.leaseId) return null;
      storedLease = { ...storedLease, offlinePermit: permit };
      return permit;
    }),
  };

  const clock = { rebase: vi.fn(async () => true) };

  function setup(pins: Record<string, string> = {}): OfflineLicencePermitService {
    TestBed.configureTestingModule({
      providers: [
        OfflineLicencePermitService,
        { provide: OfflinePermitClockIndexedDbService, useValue: clock },
        { provide: OFFLINE_PERMIT_PUBLIC_KEYS, useValue: pins },
        { provide: AuthService, useValue: { session: sessionSignal } },
        { provide: OfflineSalesDeviceSettingsStorage, useValue: settingsStorage },
        { provide: InvoiceLeaseIndexedDbService, useValue: leaseDb },
        { provide: TillContextService, useValue: { enabled: tillEnabled, selected: tillSelected } },
      ],
    });
    return TestBed.inject(OfflineLicencePermitService);
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
    sessionSignal.set({
      accessToken: 'token',
      refreshToken: 'refresh',
      accessTokenExpiresAt: '2099-01-01T00:00:00Z',
      refreshTokenExpiresAt: '2099-01-01T00:00:00Z',
      rememberMe: true,
      user: { id: userId, email: 'owner@test.com', phoneNumber: null, firstName: 'Owner', lastName: 'User', language: 'en-IN' },
      activeShopId: shopId,
      shops: [{ shopId, shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null }],
      sessionId,
    });
    tillEnabled.set(false);
    tillSelected.set(null);
    storedSettings = {
      shopId,
      deviceId,
      label: 'Counter 1',
      enabled: true,
      enabledAt: '2026-11-20T12:00:00Z',
      enabledByUserId: userId,
      enabledByUserName: 'Owner User',
      lastCompleteSnapshotAt: '2026-11-20T12:00:00Z',
      lastApiVerifiedAt: '2026-11-20T12:00:00Z',
      lastSnapshotWarningMarker: null,
      lastReservedLease: {
        leaseId,
        fiscalYear: '2026-27',
        remainingCount: 200,
        expiresAt: '2099-11-29T11:59:00.000000Z',
        licenceGrantId: deliveryFixture.leaseEvidence.grantId,
      },
      lastOfflinePermit: null,
    };
    storedLease = {
      leaseId,
      shopId,
      deviceId,
      fiscalYear: '2026-27',
      prefix: 'A/2627/',
      numberPadding: 6,
      rangeStart: 1,
      rangeEnd: 200,
      nextNumber: 1,
      remainingCount: 200,
      reservedAt: '2026-11-22T11:59:00.000000Z',
      expiresAt: '2099-11-29T11:59:00.000000Z',
      licenceEvidence: deliveryFixture.leaseEvidence,
    };
    vi.clearAllMocks();
  });

  it('resolves binding for an unexpired anchored lease belonging to the signed-in user', async () => {
    const service = setup();

    const binding = await service.resolveBinding(shopId);

    expect(binding).toEqual({
      shopId,
      userId,
      deviceId,
      fiscalYear: '2026-27',
      leaseId,
      leaseExpiresAt: '2099-11-29T11:59:00.000000Z',
      grantId: deliveryFixture.leaseEvidence.grantId,
      grantSha256: deliveryFixture.leaseEvidence.grantSha256,
      tillSessionId: null,
    });
  });

  it('includes the selected till session id only when tills are enabled', async () => {
    tillEnabled.set(true);
    tillSelected.set({ id: 'till-session-1' });
    const service = setup();

    const binding = await service.resolveBinding(shopId);
    expect(binding?.tillSessionId).toBe('till-session-1');

    tillEnabled.set(false);
    const disabled = await service.resolveBinding(shopId);
    expect(disabled?.tillSessionId).toBeNull();
  });

  it('returns null when the lease is legacy, expired, or enabled by another user', async () => {
    const service = setup();
    storedLease = { ...storedLease!, licenceEvidence: null };
    expect(await service.resolveBinding(shopId)).toBeNull();

    storedLease = { ...storedLease!, licenceEvidence: deliveryFixture.leaseEvidence, expiresAt: '2020-01-01T00:00:00Z' };
    expect(await service.resolveBinding(shopId)).toBeNull();

    storedLease = { ...storedLease!, expiresAt: '2099-01-01T00:00:00Z' };
    storedSettings = { ...storedSettings!, enabledByUserId: 'another-user' };
    expect(await service.resolveBinding(shopId)).toBeNull();
  });

  it('attaches a verified permit to the lease record and projects its display summary to settings', async () => {
    const service = setup();
    const binding = (await service.resolveBinding(shopId))!;

    const accepted = await service.acceptDeliveredPermitUnderLock(
      binding,
      deliveryFixture.issuedRecord.licencePermit,
      fields['snapshotId'],
      fields['snapshotCompletedAt'],
    );

    expect(accepted).not.toBeNull();
    expect(leaseDb.attachPermitUnderLock).toHaveBeenCalledWith(shopId, deviceId, '2026-27', accepted);
    expect(storedSettings?.lastOfflinePermit).toEqual({
      leaseId,
      grantId: deliveryFixture.leaseEvidence.grantId,
      permitId: fields['permitId'],
      snapshotId: fields['snapshotId'],
      issuedAt: fields['issuedAt'],
      notBefore: fields['notBefore'],
      validUntil: '2026-11-22T12:00:00.000000Z',
      limitingReason: 'AuthorityFreshness',
    });
  });

  it('leaves stored permit untouched when record is refused or lease was replaced', async () => {
    const service = setup();
    const binding = (await service.resolveBinding(shopId))!;

    const refused = await service.acceptDeliveredPermitUnderLock(
      binding,
      deliveryFixture.refusedRecord.licencePermit,
      fields['snapshotId'],
      fields['snapshotCompletedAt'],
    );
    expect(refused).toBeNull();
    expect(leaseDb.attachPermitUnderLock).not.toHaveBeenCalled();

    // Another tab replaced the lease with a different leaseId.
    storedLease = { ...storedLease!, leaseId: 'newer-lease' };
    const stale = await service.acceptDeliveredPermitUnderLock(
      binding,
      deliveryFixture.issuedRecord.licencePermit,
      fields['snapshotId'],
      fields['snapshotCompletedAt'],
    );
    expect(stale).toBeNull();
    expect(settingsStorage.updateSettings).not.toHaveBeenCalled();
  });

  it('adopts the signed server time as clock evidence only when the delivered signature verifies against a pin', async () => {
    const service = setup({ [permitFixture.key.keyId]: permitFixture.key.publicKeySpki });
    const binding = (await service.resolveBinding(shopId))!;

    const stored = await service.acceptDeliveredPermitUnderLock(
      binding,
      deliveryFixture.issuedRecord.licencePermit,
      fields['snapshotId'],
      fields['snapshotCompletedAt'],
    );

    expect(stored?.signedPermit).toBe(permitFixture.permit.token);
    expect(clock.rebase).toHaveBeenCalledWith({
      shopId,
      deviceId,
      userId,
      sessionId,
      permitId: fields['permitId'],
      serverObservedAt: fields['issuedAt'],
      serverObservedMs: Date.parse('2026-11-22T11:59:30.123Z'),
    });
  });

  it('still stores a delivered permit this build cannot verify, but establishes no clock evidence from it', async () => {
    const service = setup({});
    const binding = (await service.resolveBinding(shopId))!;

    const stored = await service.acceptDeliveredPermitUnderLock(
      binding,
      deliveryFixture.issuedRecord.licencePermit,
      fields['snapshotId'],
      fields['snapshotCompletedAt'],
    );

    expect(stored).not.toBeNull();
    expect(clock.rebase).not.toHaveBeenCalled();
  });

  it('keeps the stored permit when clock evidence cannot be written', async () => {
    clock.rebase.mockRejectedValueOnce(new Error('storage unavailable'));
    const service = setup({ [permitFixture.key.keyId]: permitFixture.key.publicKeySpki });
    const binding = (await service.resolveBinding(shopId))!;

    const stored = await service.acceptDeliveredPermitUnderLock(
      binding,
      deliveryFixture.issuedRecord.licencePermit,
      fields['snapshotId'],
      fields['snapshotCompletedAt'],
    );

    expect(stored).not.toBeNull();
  });
});
