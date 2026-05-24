import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { NetworkStatusService } from '../../../core/services/network-status.service';
import { OfflineSalesDeviceSettings, OfflineSalesDeviceSettingsStorage } from '../../../core/storage/offline-sales-device-settings.storage';
import { InvoiceLeaseIndexedDbService, type InvoiceLeaseSnapshot } from '../../../core/storage/invoice-lease-indexeddb.service';
import { OfflineSalesSnapshotIndexedDbService } from '../../../core/storage/offline-sales-snapshot-indexeddb.service';
import { OfflineSalesQueueSyncService } from './offline-sales-queue-sync.service';
import { OfflineSalesSnapshotSyncService } from './offline-sales-snapshot-sync.service';
import { SaleService } from './sale.service';
import type { InvoiceLeaseDto } from './sale.models';

export type OfflineSalesEnablementResult =
  | { readonly ok: true; readonly settings: OfflineSalesDeviceSettings; readonly lease: InvoiceLeaseSnapshot }
  | { readonly ok: false; readonly reason: 'UNAUTHENTICATED' | 'API_UNREACHABLE' | 'ACTIVE_SHOP_MISMATCH' | 'SNAPSHOT_INCOMPLETE' | 'LEASE_UNAVAILABLE' };

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

  async enableForShop(shopId: string, label: string): Promise<OfflineSalesEnablementResult> {
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

    // 1) Snapshot must complete.
    await this.snapshotSync.syncForShop(shopId);
    const usable = await this.snapshotDb.getUsableSnapshotInfo(shopId);
    if (!usable?.snapshotId || !usable?.completedAt) {
      return { ok: false, reason: 'SNAPSHOT_INCOMPLETE' };
    }

    // 2) Invoice lease must be usable (reserve while API reachable, then persist locally).
    let leaseDto: InvoiceLeaseDto;
    try {
      leaseDto = await firstValueFrom(this.saleService.reserveInvoiceLease({ deviceId }));
    } catch {
      return { ok: false, reason: 'LEASE_UNAVAILABLE' };
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

    // 3) Persist enabled state only once prerequisites succeed.
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
