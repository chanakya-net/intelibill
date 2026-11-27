import { OfflineSalesSnapshotIndexedDbService } from '../../../core/storage/offline-sales-snapshot-indexeddb.service';
import { OfflinePermitClockIndexedDbService } from '../../../core/storage/offline-permit-clock-indexeddb.service';
import { TestBed } from '@angular/core/testing';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { Subject, of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { InvoiceLeaseIndexedDbService } from '../../../core/storage/invoice-lease-indexeddb.service';
import { OfflineSalesDeviceSettingsStorage } from '../../../core/storage/offline-sales-device-settings.storage';
import { SubscriptionStatusService } from '../../billing/status/subscription-status.service';
import { TillContextService } from '../../tills/services/till-context.service';
import { verifyDeliveredOfflineLicencePermit } from '../utils/offline-licence-permit';
import { OFFLINE_PERMIT_PUBLIC_KEYS } from '../utils/offline-permit-signature';
import { OfflineIssuanceAuthorityService } from './offline-issuance-authority.service';
import { OfflineLicencePermitService } from './offline-licence-permit.service';
import { OfflinePermitRefreshService } from './offline-permit-refresh.service';
import { OfflineSalesSnapshotSyncService } from './offline-sales-snapshot-sync.service';
import { SaleService } from './sale.service';

const fixture = (name: string) => JSON.parse(readFileSync(resolve(process.cwd(), '../../../tests/contracts/licensing', name), 'utf8'));
const { key, permit } = fixture('offline-permit-v1.json');
const delivery = fixture('offline-permit-delivery-v1.json');
const fields = permit.fields;
const { shopId, userId, sessionId, deviceId, leaseId } = fields;
const at = '2026-11-23T00:00:00.000Z';
const preview = { schemaVersion: 1, enforced: true, shopId, authorityVerifiedAt: at, validUntil: '2026-11-24T00:00:00Z' };
const invocation = { shopId, userId, sessionId, epoch: 1, scope: { shopId, userId, epoch: 1 } };
const binding = { ...fields, fiscalYear: '2026-27', leaseExpiresAt: '2026-11-25T00:00:00Z' };

function setup() {
  const stored = verifyDeliveredOfflineLicencePermit(delivery.issuedRecord.licencePermit, { binding, ...fields })!;
  const lease = { shopId, deviceId, leaseId, fiscalYear: binding.fiscalYear, expiresAt: binding.leaseExpiresAt, remainingCount: 37,
    licenceEvidence: delivery.leaseEvidence, offlinePermit: stored };
  const settings = { enabled: true, enabledByUserId: userId, deviceId, lastReservedLease: lease, permitRefreshNotice: undefined as string | undefined };
  const capture = vi.fn(() => invocation);
  const isCurrent = vi.fn(() => true);
  const read = vi.fn(() => of(preview));
  const reserve = vi.fn(() => of({ ...lease, licenceEvidence: null }));
  const stream = vi.fn();
  const load = vi.fn(async () => lease);
  const claim = vi.fn(async (): Promise<unknown> => null);
  const update = vi.fn((_shopId: string, fn: (current: typeof settings) => typeof settings) => Object.assign(settings, fn(settings)));
  const tillId = vi.fn((): string | null => null);
  const checkpoint = vi.fn(async () => true);
  const member = { role: 'Owner', shopId };
  TestBed.configureTestingModule({ providers: [
    { provide: AuthService, useValue: { session: () => ({ shops: [member] }) } },
    { provide: TillContextService, useValue: { capability: () => ({ enabled: false }), enabled: () => !!tillId(), selected: () => ({ id: tillId() }) } },
    { provide: OfflineSalesDeviceSettingsStorage, useValue: { loadSettings: () => settings, updateSettings: update } },
    { provide: OfflineIssuanceAuthorityService, useValue: { capture, isCurrent } },
    { provide: InvoiceLeaseIndexedDbService, useValue: { loadLease: load, loadPendingClaim: claim } },
    { provide: OfflineSalesSnapshotIndexedDbService, useValue: { getUsableSnapshotId: () => fields.snapshotId } },
    { provide: OfflinePermitClockIndexedDbService, useValue: { hasCheckpoint: checkpoint } },
    { provide: SaleService, useValue: { reserveInvoiceLease: reserve } },
    { provide: OfflineLicencePermitService, useValue: { bindingForLease: () => null } },
    { provide: SubscriptionStatusService, useValue: { tradingAuthority: read } },
    { provide: OfflineSalesSnapshotSyncService, useValue: { syncForShop: stream } },
    { provide: OFFLINE_PERMIT_PUBLIC_KEYS, useValue: { [key.keyId]: key.publicKeySpki } },
  ] });
  return { service: TestBed.inject(OfflinePermitRefreshService), settings, member, lease, claim, isCurrent,
    read, reserve, stream, update, tillId, checkpoint };
}

describe('verified reconnect scope and reservation budget', () => {
  beforeEach(() => {
    vi.spyOn(Date, 'now').mockReturnValue(Date.parse(at) + 10);
    vi.stubGlobal('navigator', { locks: { request: (_name: string, _opts: unknown, work: () => unknown) => work() } });
  });
  afterEach(() => { TestBed.resetTestingModule(); vi.unstubAllGlobals(); vi.restoreAllMocks(); });

  it.each(['disabled', 'different user', 'staff', 'no locks', 'edited permit'] as const)(
    'sends no request for %s', async condition => {
      const h = setup();
      if (condition === 'disabled') h.settings.enabled = false;
      if (condition === 'different user') h.settings.enabledByUserId = 'other';
      if (condition === 'staff') h.member.role = 'Staff';
      if (condition === 'no locks') vi.stubGlobal('navigator', {});
      if (condition === 'edited permit') h.lease.offlinePermit = { ...h.lease.offlinePermit, validUntil: at };
      await h.service.refresh(shopId);
      expect(h.read).not.toHaveBeenCalled();
      expect(h.reserve).not.toHaveBeenCalled();
    });

  it.each(['restricted', 'unverified', 'suspended', 'verified', undefined])(
    'healthy non-reserving contact updates notice %s without spending numbers', async prior => {
      const h = setup();
      vi.spyOn(Date, 'now').mockReturnValue(Date.parse(fields.issuedAt) + 1);
      h.settings.permitRefreshNotice = prior;
      h.read.mockReturnValue(of({ ...preview, authorityVerifiedAt: fields.issuedAt, validUntil: fields.validUntil }));
      await h.service.refresh(shopId);
      expect(h.settings.permitRefreshNotice).toBe(prior === 'verified' ? 'verified' : undefined);
      expect(h.reserve).not.toHaveBeenCalled();
    });

  it('clears a transient warning when the same verified observation still covers this permit', async () => {
    const h = setup();
    vi.spyOn(Date, 'now').mockReturnValue(Date.parse(fields.issuedAt) + 1);
    h.settings.permitRefreshNotice = 'unverified';
    h.read.mockReturnValue(of({ ...preview, authorityVerifiedAt: fields.authorityVerifiedAt, validUntil: fields.validUntil }));
    await h.service.refresh(shopId);
    expect(h.settings.permitRefreshNotice).toBeUndefined();
    expect(h.reserve).not.toHaveBeenCalled();
  });

  it('marks an expired permit unverified when authority is unchanged', async () => {
    const h = setup();
    h.settings.permitRefreshNotice = 'verified';
    h.read.mockReturnValue(of({ ...preview, authorityVerifiedAt: fields.authorityVerifiedAt, validUntil: fields.validUntil }));
    await h.service.refresh(shopId);
    expect(h.settings.permitRefreshNotice).toBe('unverified');
    expect(h.reserve).not.toHaveBeenCalled();
  });

  it.each(['new till', 'missing checkpoint', 'interrupted settings'])('repairs %s on the held range', async condition => {
    const h = setup();
    vi.spyOn(Date, 'now').mockReturnValue(Date.parse(fields.issuedAt) + 1);
    if (condition === 'new till') h.tillId.mockReturnValue('new-till');
    else h.checkpoint.mockResolvedValue(false);
    if (condition === 'interrupted settings') h.settings.lastReservedLease = { ...h.lease, leaseId: 'previous' };
    h.read.mockReturnValue(of({ ...preview, authorityVerifiedAt: fields.authorityVerifiedAt, validUntil: fields.validUntil }));
    await h.service.refresh(shopId);
    expect(h.settings.lastReservedLease.leaseId).toBe(leaseId);
    expect(h.stream).toHaveBeenCalledWith(shopId);
    expect(h.reserve).not.toHaveBeenCalled();
  });

  it('drops the authority answer when the captured session or epoch has changed', async () => {
    const h = setup();
    const answer = new Subject<typeof preview>();
    h.read.mockReturnValue(answer);
    const finished = h.service.refresh(shopId);
    await vi.waitFor(() => expect(h.read).toHaveBeenCalledOnce());
    h.isCurrent.mockReturnValue(false);
    answer.next(preview);
    await finished;
    expect(h.reserve).not.toHaveBeenCalled();
    expect(h.update).not.toHaveBeenCalled();
  });

  it('does not spend numbers while a previous sale still has an unsettled claim', async () => {
    const h = setup();
    h.claim.mockResolvedValue({ clientSaleId: 'earlier' });
    expect(await h.service.refresh(shopId)).toBe(true);
    expect(h.reserve).not.toHaveBeenCalled();
  });

  it('limits the reservation to the remaining block and discards a legacy response', async () => {
    const h = setup();
    await h.service.refresh(shopId);
    expect(h.reserve).toHaveBeenCalledWith({ deviceId, blockSize: 37 }, invocation.scope);
    expect(h.update.mock.invocationCallOrder[0]).toBeLessThan(h.reserve.mock.invocationCallOrder[0]);
    expect(h.stream).not.toHaveBeenCalled();
  });
});
