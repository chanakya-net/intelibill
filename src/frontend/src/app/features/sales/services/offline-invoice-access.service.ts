import { Injectable, inject } from '@angular/core';

import { AuthService } from '../../../core/auth/auth.service';
import { OfflineSalesDeviceSettingsStorage } from '../../../core/storage/offline-sales-device-settings.storage';
import type { OfflineQueuedSaleRecord } from './offline-sale-core.types';
import { OfflineSalesQueueIndexedDbService } from './offline-sales-queue-indexeddb.service';

export type OfflineInvoiceAccessDenial =
  | 'NO_SESSION'
  | 'NO_DEVICE'
  | 'GRACE_UNAVAILABLE'
  | 'NOT_FOUND'
  | 'NOT_OWNER'
  | 'SCOPE_CHANGED';

export type OfflineInvoiceReadResult =
  | { readonly granted: true; readonly record: OfflineQueuedSaleRecord }
  | { readonly granted: false; readonly reason: OfflineInvoiceAccessDenial };

/**
 * Reads a queued offline invoice for printing, and only for the person allowed to see it: the signed-in user who
 * issued it (or, on a live session, the shop's owner or manager), in the shop and on the device it was issued from,
 * with a valid token or, when the API cannot be reached, the device's existing offline grace. Nothing outside the
 * active session's own shop and device is ever consulted, so a denied read never falls back to another shop's data.
 */
@Injectable({ providedIn: 'root' })
export class OfflineInvoiceAccessService {
  private readonly auth = inject(AuthService);
  private readonly deviceSettings = inject(OfflineSalesDeviceSettingsStorage);
  private readonly queueDb = inject(OfflineSalesQueueIndexedDbService);

  async read(clientSaleId: string): Promise<OfflineInvoiceReadResult> {
    const session = this.auth.session();
    const userId = session?.user.id ?? '';
    const shopId = session?.activeShopId ?? '';
    if (!userId || !shopId) return deny('NO_SESSION');

    const deviceId = this.deviceSettings.loadSettings(shopId)?.deviceId ?? '';
    if (!deviceId) return deny('NO_DEVICE');

    if (!this.auth.isAuthenticated() && !(await this.auth.canUseOfflineSalesAuthGrace())) {
      return deny('GRACE_UNAVAILABLE');
    }

    const record = await this.queueDb.getQueuedSale(shopId, deviceId, clientSaleId);
    // The session can change while IndexedDB answers (sign-out, another account, a shop switch in another tab): a
    // read started for one scope is never handed to another.
    if (!this.isSameScope(userId, shopId)) return deny('SCOPE_CHANGED');
    if (!record) return deny('NOT_FOUND');

    const payload = record.payload;
    const belongsHere =
      record.shopId === shopId && record.deviceId === deviceId && payload.shopId === shopId && payload.deviceId === deviceId;
    if (!belongsHere) return deny('NOT_FOUND');

    // Payloads queued before issuers were recorded name none; their shop and device have been checked above. Another
    // cashier's invoice is for an owner or manager reviewing the device's queue, and only on a live session: offline
    // grace never widens what a person may see.
    if (payload.issuedByUserId && payload.issuedByUserId !== userId && !this.canReviewDeviceQueue(shopId)) {
      return deny('NOT_OWNER');
    }

    return { granted: true, record };
  }

  async canRead(clientSaleId: string): Promise<boolean> {
    return (await this.read(clientSaleId)).granted;
  }

  private canReviewDeviceQueue(shopId: string): boolean {
    if (!this.auth.isAuthenticated()) return false;
    const role = this.auth.session()?.shops?.find((shop) => shop.shopId === shopId)?.role?.trim().toLowerCase();
    return role === 'owner' || role === 'manager';
  }

  private isSameScope(userId: string, shopId: string): boolean {
    const session = this.auth.session();
    return session?.user.id === userId && session.activeShopId === shopId;
  }
}

function deny(reason: OfflineInvoiceAccessDenial): OfflineInvoiceReadResult {
  return { granted: false, reason };
}
