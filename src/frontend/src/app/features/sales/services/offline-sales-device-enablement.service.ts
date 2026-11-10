import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { NetworkStatusService } from '../../../core/services/network-status.service';
import { OfflinePackService, type OfflinePackFailure } from '../../../core/service-worker/offline-pack.service';
import { OfflineSalesDeviceSettings, OfflineSalesDeviceSettingsStorage } from '../../../core/storage/offline-sales-device-settings.storage';
import { InvoiceLeaseIndexedDbService, type InvoiceLeaseSnapshot } from '../../../core/storage/invoice-lease-indexeddb.service';
import { OfflineSalesSnapshotIndexedDbService } from '../../../core/storage/offline-sales-snapshot-indexeddb.service';
import { OfflineSalesQueueSyncService } from './offline-sales-queue-sync.service';
import { OfflineSalesSnapshotSyncService } from './offline-sales-snapshot-sync.service';
import { SaleService } from './sale.service';
import type { InvoiceLeaseDto } from './sale.models';

export type OfflineSalesEnablementResult =
  | { readonly ok: true; readonly settings: OfflineSalesDeviceSettings; readonly lease: InvoiceLeaseSnapshot }
  | {
      readonly ok: false;
      readonly reason:
        | 'UNAUTHENTICATED'
        | 'API_UNREACHABLE'
        | 'ACTIVE_SHOP_MISMATCH'
        | 'APP_PACK_INCOMPLETE'
        | 'SNAPSHOT_INCOMPLETE'
        | 'LEASE_UNAVAILABLE';
      /** The server's error code when it refused the lease (for example an undeclared GST registration). */
      readonly errorCode?: string;
      /** Why the app files could not be made available offline (`APP_PACK_INCOMPLETE` only). */
      readonly packFailure?: OfflinePackFailure;
    };

/** The step enabling is on, for the entry points to show. */
export type OfflineSalesEnablementStep = 'appPack' | 'snapshot' | 'lease';

@Injectable({ providedIn: 'root' })
export class OfflineSalesDeviceEnablementService {
  private readonly auth = inject(AuthService);
  private readonly networkStatus = inject(NetworkStatusService);
  private readonly settingsStorage = inject(OfflineSalesDeviceSettingsStorage);
  private readonly snapshotSync = inject(OfflineSalesSnapshotSyncService);
  private readonly snapshotDb = inject(OfflineSalesSnapshotIndexedDbService);
  private readonly saleService = inject(SaleService);
  private readonly leaseDb = inject(InvoiceLeaseIndexedDbService);
  private readonly queueSync = inject(OfflineSalesQueueSyncService);
  private readonly offlinePack = inject(OfflinePackService);

  private readonly stepSignal = signal<OfflineSalesEnablementStep | null>(null);
  /** The step an enable in progress is on; null when none runs. */
  readonly step = this.stepSignal.asReadonly();

  /**
   * Enables offline billing for the active shop on this device. In order: the app's own files for
   * offline use (the offline pack: downloaded and verified complete, see `OfflinePackService`), a
   * complete data snapshot, then an invoice lease. The enabled state is saved only when all three
   * succeed; when one fails after the pack step, the pack flag goes back to off unless another shop
   * on this device still needs it.
   */
  async enableForShop(shopId: string, label: string): Promise<OfflineSalesEnablementResult> {
    let packRequested = false;
    try {
      const result = await this.enable(shopId, label, () => (packRequested = true));
      if (!result.ok && packRequested) {
        await this.offlinePack.release();
      }
      return result;
    } finally {
      this.stepSignal.set(null);
    }
  }

  private async enable(shopId: string, label: string, onPackRequested: () => void): Promise<OfflineSalesEnablementResult> {
    const session = this.auth.session();
    if (!session || !shopId) {
      return { ok: false, reason: 'UNAUTHENTICATED' };
    }
    if (session.activeShopId !== shopId) {
      return { ok: false, reason: 'ACTIVE_SHOP_MISMATCH' };
    }

    await this.networkStatus.checkConnectivity();
    if (!this.networkStatus.canReachApi()) {
      return { ok: false, reason: 'API_UNREACHABLE' };
    }

    const deviceId = this.settingsStorage.getOrCreateDeviceId(shopId);
    if (!deviceId) {
      return { ok: false, reason: 'API_UNREACHABLE' };
    }

    // 1) The app's files must be on this device, complete, before anything relies on them.
    this.stepSignal.set('appPack');
    onPackRequested();
    const pack = await this.offlinePack.prepare();
    if (!pack.ok) {
      return { ok: false, reason: 'APP_PACK_INCOMPLETE', packFailure: pack.failure };
    }

    // 2) Snapshot must complete.
    this.stepSignal.set('snapshot');
    await this.snapshotSync.syncForShop(shopId);
    const usable = await this.snapshotDb.getUsableSnapshotInfo(shopId);
    if (!usable?.snapshotId || !usable?.completedAt) {
      return { ok: false, reason: 'SNAPSHOT_INCOMPLETE' };
    }

    // 3) Invoice lease must be usable (reserve while API reachable, then persist locally).
    this.stepSignal.set('lease');
    let leaseDto: InvoiceLeaseDto;
    try {
      leaseDto = await firstValueFrom(this.saleService.reserveInvoiceLease({ deviceId }));
    } catch (error) {
      const errorCode = (error as { error?: { title?: unknown } } | null)?.error?.title;
      return typeof errorCode === 'string'
        ? { ok: false, reason: 'LEASE_UNAVAILABLE', errorCode }
        : { ok: false, reason: 'LEASE_UNAVAILABLE' };
    }

    const lease: InvoiceLeaseSnapshot = {
      ...leaseDto,
      shopId: leaseDto.shopId,
      deviceId: leaseDto.deviceId,
      fiscalYear: leaseDto.fiscalYear,
      prefix: leaseDto.prefix,
      numberPadding: leaseDto.numberPadding,
      rangeStart: leaseDto.rangeStart,
      rangeEnd: leaseDto.rangeEnd,
      nextNumber: leaseDto.nextNumber,
      remainingCount: leaseDto.remainingCount,
      reservedAt: leaseDto.reservedAt,
      expiresAt: leaseDto.expiresAt,
      leaseId: leaseDto.leaseId,
    };

    if (lease.remainingCount <= 0) {
      return { ok: false, reason: 'LEASE_UNAVAILABLE' };
    }

    await this.leaseDb.saveLease(lease);

    // 4) Persist enabled state only once prerequisites succeed.
    const enabledAt = new Date().toISOString();
    const enabledByUserName = `${session.user.firstName} ${session.user.lastName}`.trim();
    const lastVerifiedAt = this.networkStatus.lastVerifiedAt()?.toISOString() ?? new Date().toISOString();

    const settings = this.settingsStorage.updateSettings(shopId, (current) => ({
      ...current,
      label: label ?? current.label ?? '',
      enabled: true,
      enabledAt,
      enabledByUserId: session.user.id,
      enabledByUserName,
      lastCompleteSnapshotAt: usable.completedAt,
      lastApiVerifiedAt: lastVerifiedAt,
      lastReservedLease: {
        leaseId: lease.leaseId,
        fiscalYear: lease.fiscalYear,
        remainingCount: lease.remainingCount,
        expiresAt: lease.expiresAt,
      },
    }));

    if (!settings) {
      return { ok: false, reason: 'UNAUTHENTICATED' };
    }

    void this.queueSync.syncForShop(settings.shopId, settings.deviceId);

    return { ok: true, settings, lease };
  }
}
