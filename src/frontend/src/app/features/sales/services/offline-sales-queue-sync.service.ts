import { Injectable, Injector, effect, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { NetworkStatusService } from '../../../core/services/network-status.service';
import { OfflineSalesDeviceSettingsStorage } from '../../../core/storage/offline-sales-device-settings.storage';
import { OfflineQueuedSaleRecord, OfflineQueueSyncResult } from './offline-sale-core.types';
import { OfflineSalesQueueIndexedDbService } from './offline-sales-queue-indexeddb.service';
import { SaleService } from './sale.service';
import type { OfflineSaleSyncResultDto } from './sale.models';
import {
  OFFLINE_SALES_SYNC_BATCH_SIZE,
  addOfflineSyncResultToTotals,
  buildSyncRequestFromQueuedSale,
  isOfflineSaleSyncRetryable,
  mapOfflineSyncResult,
  mapToVisibleQueueCounts,
  OfflineSalesSyncRunResult,
  OfflineSalesVisibleQueueCounts,
} from '../utils/offline-sale-sync.mapper';

const EMPTY_COUNTS: OfflineSalesVisibleQueueCounts = {
  pending: 0,
  syncing: 0,
  failed: 0,
  warning: 0,
  needsReview: 0,
  totalVisible: 0,
};

@Injectable({ providedIn: 'root' })
export class OfflineSalesQueueSyncService {
  private readonly auth = inject(AuthService);
  private readonly networkStatus = inject(NetworkStatusService);
  private readonly settingsStorage = inject(OfflineSalesDeviceSettingsStorage);
  private readonly queueDb = inject(OfflineSalesQueueIndexedDbService);
  private readonly saleService = inject(SaleService);
  private readonly injector = inject(Injector);

  private readonly locks = new Map<string, Promise<OfflineSalesSyncRunResult>>();
  private autoWatchStarted = false;
  private previousReachable = false;
  private previousShopId: string | null = null;

  readonly visibleCounts = signal<OfflineSalesVisibleQueueCounts>(EMPTY_COUNTS);

  startAutoSyncWatchers(): void {
    if (this.autoWatchStarted) return;
    this.autoWatchStarted = true;
    this.previousReachable = this.networkStatus.canReachApi();
    this.previousShopId = this.auth.session()?.activeShopId ?? null;

    effect(() => {
      const session = this.auth.session();
      const shopId = session?.activeShopId ?? null;
      const reachable = this.networkStatus.canReachApi();
      const becameReachable = reachable && !this.previousReachable;
      const switchedShop = !!shopId && shopId !== this.previousShopId;

      this.previousReachable = reachable;
      this.previousShopId = shopId;

      if (!shopId) {
        this.visibleCounts.set(EMPTY_COUNTS);
        return;
      }

      void this.refreshActiveStatusCounts();

      if (reachable && (becameReachable || switchedShop)) {
        void this.syncActiveShop();
      }
    }, { injector: this.injector });
  }

  runStartupSync(): void {
    void this.queueDb.deleteOldSyncedRecords();
    void this.networkStatus.checkConnectivity().then(() => {
      if (this.networkStatus.canReachApi()) {
        void this.syncActiveShop();
      }
    });
  }

  async syncActiveShop(): Promise<OfflineSalesSyncRunResult> {
    if (!this.networkStatus.canReachApi()) {
      return { attemptedCount: 0, syncedCount: 0, warningCount: 0, needsReviewCount: 0, failedCount: 0, skippedReason: 'API_UNREACHABLE' };
    }

    const session = this.auth.session();
    const shopId = session?.activeShopId ?? null;
    if (!shopId) {
      this.visibleCounts.set(EMPTY_COUNTS);
      return { attemptedCount: 0, syncedCount: 0, warningCount: 0, needsReviewCount: 0, failedCount: 0, skippedReason: 'NO_ACTIVE_SHOP' };
    }

    const settings = this.settingsStorage.loadSettings(shopId);
    if (!settings?.deviceId) {
      return { attemptedCount: 0, syncedCount: 0, warningCount: 0, needsReviewCount: 0, failedCount: 0, skippedReason: 'NO_DEVICE' };
    }

    return await this.syncForShop(shopId, settings.deviceId);
  }

  async retryActiveShop(): Promise<OfflineSalesSyncRunResult> {
    const session = this.auth.session();
    const shopId = session?.activeShopId ?? null;
    if (!shopId) {
      this.visibleCounts.set(EMPTY_COUNTS);
      return { attemptedCount: 0, syncedCount: 0, warningCount: 0, needsReviewCount: 0, failedCount: 0, skippedReason: 'NO_ACTIVE_SHOP' };
    }

    const settings = this.settingsStorage.loadSettings(shopId);
    if (!settings?.deviceId) {
      return { attemptedCount: 0, syncedCount: 0, warningCount: 0, needsReviewCount: 0, failedCount: 0, skippedReason: 'NO_DEVICE' };
    }

    return await this.retryForShop(shopId, settings.deviceId);
  }

  async retryQueuedSale(shopId: string, deviceId: string, clientSaleId: string): Promise<OfflineSalesSyncRunResult> {
    if (!this.networkStatus.canReachApi()) {
      return { attemptedCount: 0, syncedCount: 0, warningCount: 0, needsReviewCount: 0, failedCount: 0, skippedReason: 'API_UNREACHABLE' };
    }

    const lockKey = this.buildLockKey(shopId, deviceId);
    const existing = this.locks.get(lockKey);
    if (existing) return await existing;

    const run = this.queueDb.getQueuedSale(shopId, deviceId, clientSaleId)
      .then((record) => {
        if (!record || !isOfflineSaleSyncRetryable(record.status)) {
          return { attemptedCount: 0, syncedCount: 0, warningCount: 0, needsReviewCount: 0, failedCount: 0, skippedReason: 'NOT_RETRYABLE' } satisfies OfflineSalesSyncRunResult;
        }

        return this.syncRecords(shopId, deviceId, [record]);
      });
    this.locks.set(lockKey, run);

    try {
      return await run;
    } finally {
      this.locks.delete(lockKey);
    }
  }

  async retryForShop(shopId: string, deviceId: string): Promise<OfflineSalesSyncRunResult> {
    if (!this.networkStatus.canReachApi()) {
      return { attemptedCount: 0, syncedCount: 0, warningCount: 0, needsReviewCount: 0, failedCount: 0, skippedReason: 'API_UNREACHABLE' };
    }

    const lockKey = this.buildLockKey(shopId, deviceId);
    const existing = this.locks.get(lockKey);
    if (existing) return await existing;

    const run = this.queueDb.getRetryableSales(shopId, deviceId)
      .then((records) => this.syncRecords(shopId, deviceId, records));
    this.locks.set(lockKey, run);

    try {
      return await run;
    } finally {
      this.locks.delete(lockKey);
    }
  }

  async syncForShop(shopId: string, deviceId: string): Promise<OfflineSalesSyncRunResult> {
    if (!this.networkStatus.canReachApi()) {
      return { attemptedCount: 0, syncedCount: 0, warningCount: 0, needsReviewCount: 0, failedCount: 0, skippedReason: 'API_UNREACHABLE' };
    }

    const lockKey = this.buildLockKey(shopId, deviceId);
    const existing = this.locks.get(lockKey);
    if (existing) return await existing;

    const run = this.queueDb.getPendingSales(shopId, deviceId)
      .then((records) => this.syncRecords(shopId, deviceId, records));
    this.locks.set(lockKey, run);

    try {
      return await run;
    } finally {
      this.locks.delete(lockKey);
    }
  }

  async refreshActiveStatusCounts(): Promise<OfflineSalesVisibleQueueCounts> {
    const session = this.auth.session();
    const shopId = session?.activeShopId ?? null;
    if (!shopId) {
      this.visibleCounts.set(EMPTY_COUNTS);
      return EMPTY_COUNTS;
    }

    const settings = this.settingsStorage.loadSettings(shopId);
    if (!settings?.deviceId) {
      this.visibleCounts.set(EMPTY_COUNTS);
      return EMPTY_COUNTS;
    }

    const counts = await this.queueDb.getStatusCounts(shopId, settings.deviceId);
    const visible = mapToVisibleQueueCounts(counts);
    this.visibleCounts.set(visible);
    return visible;
  }

  async cleanupSyncedRecords(): Promise<number> {
    return await this.queueDb.deleteOldSyncedRecords();
  }

  private async syncRecords(
    shopId: string,
    deviceId: string,
    records: readonly OfflineQueuedSaleRecord[],
  ): Promise<OfflineSalesSyncRunResult> {
    const totals = {
      attemptedCount: 0,
      syncedCount: 0,
      warningCount: 0,
      needsReviewCount: 0,
      failedCount: 0,
    };

    for (let start = 0; start < records.length; start += OFFLINE_SALES_SYNC_BATCH_SIZE) {
      const batch = records.slice(start, start + OFFLINE_SALES_SYNC_BATCH_SIZE);
      const marked: OfflineQueuedSaleRecord[] = [];

      for (const record of batch) {
        const syncing = await this.queueDb.markSyncInProgress(shopId, deviceId, record.clientSaleId);
        if (syncing) marked.push(syncing);
      }

      if (marked.length === 0) continue;
      totals.attemptedCount += marked.length;

      try {
        const response = await firstValueFrom(this.saleService.syncOfflineSales({
          deviceId,
          sales: marked.map((record) => buildSyncRequestFromQueuedSale(record)),
        }));
        const byClientSaleId = new Map(response.results.map((result) => [result.clientSaleId, result]));

        for (const record of marked) {
          const result = byClientSaleId.get(record.clientSaleId);
          const applied = await this.applyBackendResult(shopId, deviceId, record, result);
          addOfflineSyncResultToTotals(totals, applied.status);
        }
      } catch (error) {
        const message = error instanceof Error ? error.message : 'Offline sale sync failed.';
        for (const record of marked) {
          await this.queueDb.applySyncResult(shopId, deviceId, record.clientSaleId, {
            status: 'Failed',
            errorCode: 'offline_sync.request_failed',
            errorMessage: message,
          });
          totals.failedCount += 1;
        }
      }
    }

    if (totals.attemptedCount > 0) {
      await this.queueDb.deleteOldSyncedRecords();
    }
    await this.refreshActiveStatusCounts();

    return totals;
  }

  private async applyBackendResult(
    shopId: string,
    deviceId: string,
    record: OfflineQueuedSaleRecord,
    result: OfflineSaleSyncResultDto | undefined,
  ): Promise<OfflineQueueSyncResult> {
    const mapped = mapOfflineSyncResult(result);
    await this.queueDb.applySyncResult(shopId, deviceId, record.clientSaleId, mapped);
    return mapped;
  }

  private buildLockKey(shopId: string, deviceId: string): string {
    return `${shopId}::${deviceId}`;
  }
}
