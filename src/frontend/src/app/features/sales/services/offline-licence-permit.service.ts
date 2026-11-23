import { Injectable, inject } from '@angular/core';

import { AuthService } from '../../../core/auth/auth.service';
import { InvoiceLeaseIndexedDbService, type InvoiceLeaseSnapshot } from '../../../core/storage/invoice-lease-indexeddb.service';
import { OfflinePermitClockIndexedDbService } from '../../../core/storage/offline-permit-clock-indexeddb.service';
import { OfflineSalesDeviceSettingsStorage } from '../../../core/storage/offline-sales-device-settings.storage';
import { TillContextService } from '../../tills/services/till-context.service';
import {
  isValidLeaseLicenceEvidence,
  parseInstantNanos,
  toOfflinePermitDisplaySummary,
  verifyDeliveredOfflineLicencePermit,
  type OfflineLicencePermitBinding,
  type StoredOfflineLicencePermit,
} from '../utils/offline-licence-permit';
import { OFFLINE_PERMIT_PUBLIC_KEYS, verifyOfflinePermitSignature } from '../utils/offline-permit-signature';

@Injectable({ providedIn: 'root' })
export class OfflineLicencePermitService {
  private readonly auth = inject(AuthService);
  private readonly settingsStorage = inject(OfflineSalesDeviceSettingsStorage);
  private readonly leaseDb = inject(InvoiceLeaseIndexedDbService);
  private readonly tillContext = inject(TillContextService, { optional: true });
  private readonly clock = inject(OfflinePermitClockIndexedDbService);
  private readonly publicKeys = inject(OFFLINE_PERMIT_PUBLIC_KEYS);

  async resolveBinding(shopId: string): Promise<OfflineLicencePermitBinding | null> {
    const session = this.auth.session();
    if (!session || session.activeShopId !== shopId || !session.user?.id) return null;

    const settings = this.settingsStorage.loadSettings(shopId);
    if (
      !settings?.deviceId ||
      !settings.lastReservedLease?.fiscalYear ||
      settings.enabledByUserId !== session.user.id
    ) {
      return null;
    }

    const lease = await this.leaseDb.loadLease(shopId, settings.deviceId, settings.lastReservedLease.fiscalYear);
    return this.bindingForLease(lease, shopId, session.user.id, settings.deviceId);
  }

  bindingForLease(
    lease: InvoiceLeaseSnapshot | null,
    shopId: string,
    userId: string,
    deviceId: string,
  ): OfflineLicencePermitBinding | null {
    if (
      !lease ||
      lease.shopId !== shopId ||
      lease.deviceId !== deviceId ||
      !isValidLeaseLicenceEvidence(lease.licenceEvidence)
    ) {
      return null;
    }

    const expiresMs = Date.parse(lease.expiresAt);
    if (!Number.isFinite(expiresMs) || expiresMs <= Date.now()) return null;

    const tillSessionId = this.tillContext?.enabled() ? (this.tillContext.selected()?.id ?? null) : null;
    return {
      shopId,
      userId,
      deviceId,
      fiscalYear: lease.fiscalYear,
      leaseId: lease.leaseId,
      leaseExpiresAt: lease.expiresAt,
      grantId: lease.licenceEvidence.grantId,
      grantSha256: lease.licenceEvidence.grantSha256,
      tillSessionId,
    };
  }

  async acceptDeliveredPermitUnderLock(
    binding: OfflineLicencePermitBinding,
    record: unknown,
    snapshotId: string,
    snapshotCompletedAt: string,
  ): Promise<StoredOfflineLicencePermit | null> {
    const session = this.auth.session();
    if (!session || session.activeShopId !== binding.shopId || session.user?.id !== binding.userId) return null;

    const verified = verifyDeliveredOfflineLicencePermit(record, {
      binding,
      snapshotId,
      snapshotCompletedAt,
      sessionId: session.sessionId ?? null,
    });
    if (!verified) return null;

    const stored = await this.leaseDb.attachPermitUnderLock(binding.shopId, binding.deviceId, binding.fiscalYear, verified);
    if (!stored) return null;

    const summary = toOfflinePermitDisplaySummary(stored);
    this.settingsStorage.updateSettings(binding.shopId, (current) =>
      current.lastReservedLease?.leaseId === stored.leaseId ? { ...current, lastOfflinePermit: summary } : current,
    );
    await this.establishClockEvidence(stored, session.sessionId ?? null);
    return stored;
  }

  /**
   * A permit that just arrived over the authenticated stream carries a fresh signed server time. Only when its
   * signature verifies against this build's pins, for this very session, does that time become the clock checkpoint
   * offline issuance is judged against. A failure here never undoes the stored permit; issuance simply stays refused.
   */
  private async establishClockEvidence(stored: StoredOfflineLicencePermit, sessionId: string | null): Promise<void> {
    try {
      const verified = await verifyOfflinePermitSignature(stored.signedPermit, this.publicKeys);
      const issuedAt = verified ? parseInstantNanos(verified.payload.issuedAt) : null;
      if (!verified || issuedAt === null || !sessionId || verified.payload.sessionId !== sessionId) return;
      await this.clock.rebase({
        shopId: verified.payload.shopId,
        deviceId: verified.payload.deviceId,
        userId: verified.payload.userId,
        sessionId: verified.payload.sessionId,
        permitId: verified.payload.permitId,
        serverObservedAt: verified.payload.issuedAt,
        serverObservedMs: Number(issuedAt / 1_000_000n),
      });
    } catch {
      // Storage or crypto unavailable: no checkpoint, so issuance asks for an online verification.
    }
  }
}
