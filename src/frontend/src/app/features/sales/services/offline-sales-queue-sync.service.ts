import { Injectable, Injector, effect, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { NetworkStatusService } from '../../../core/services/network-status.service';
import { OfflineSalesDeviceSettingsStorage } from '../../../core/storage/offline-sales-device-settings.storage';
import { OfflineArithmeticMismatch, OfflineQueuedSaleRecord, OfflineQueueSyncResult } from './offline-sale-core.types';
import { OfflineSalesQueueIndexedDbService } from './offline-sales-queue-indexeddb.service';
import { SaleService } from './sale.service';
import type { OfflineSaleSyncResultDto } from './sale.models';
import {
  OFFLINE_SALES_SYNC_BATCH_SIZE,
  addOfflineSyncResultToTotals,
  buildSyncRequestFromQueuedSale,
  isOfflineSaleSyncRetryable,
  isSubmittableBy,
  mapOfflineSyncResult,
  mapToVisibleQueueCounts,
  OfflineSalesSyncRunResult,
  OfflineSalesVisibleQueueCounts,
} from '../utils/offline-sale-sync.mapper';

export interface OfflineReviewSale {
  readonly clientSaleId: string;
  readonly invoiceNumber: string;
  readonly soldAt: string;
  readonly errorCode: string | null;
  readonly errorMessage: string | null;
  readonly reviewCaseId: string | null;
  readonly mismatches: readonly OfflineArithmeticMismatch[];
}

/**
 * An unsent (Pending or Failed) invoice issued by someone else; it waits for that user to sign in on this device,
 * or for an owner or manager to recover it from a support bundle.
 */
export interface OfflineAwaitingIssuerSale {
  readonly clientSaleId: string;
  readonly invoiceNumber: string;
  readonly soldAt: string;
  readonly issuedByUserId: string;
  readonly status: 'Pending' | 'Failed';
}

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
  /** Invoices the server held for review. They are never resent or recalculated here; the frozen payload stays as issued. */
  readonly reviewSales = signal<readonly OfflineReviewSale[]>([]);
  /** Unsent (Pending or Failed) invoices issued by another user: never submitted from this session. */
  readonly awaitingIssuerSales = signal<readonly OfflineAwaitingIssuerSale[]>([]);
  /** Every record on this device that the server has not accepted yet (any issuer, any unsynced status), oldest first. */
  readonly unsentSales = signal<readonly OfflineQueuedSaleRecord[]>([]);

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
        this.reviewSales.set([]);
        this.awaitingIssuerSales.set([]);
        this.unsentSales.set([]);
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
        if (!record || !isOfflineSaleSyncRetryable(record.status) || !isSubmittableBy(record, this.currentUserId())) {
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
      this.reviewSales.set([]);
      this.awaitingIssuerSales.set([]);
      this.unsentSales.set([]);
      return EMPTY_COUNTS;
    }

    const settings = this.settingsStorage.loadSettings(shopId);
    if (!settings?.deviceId) {
      this.visibleCounts.set(EMPTY_COUNTS);
      this.reviewSales.set([]);
      this.awaitingIssuerSales.set([]);
      this.unsentSales.set([]);
      return EMPTY_COUNTS;
    }

    const counts = await this.queueDb.getStatusCounts(shopId, settings.deviceId);
    const visible = mapToVisibleQueueCounts(counts);
    this.visibleCounts.set(visible);
    const needsReview = visible.needsReview > 0;
    const hasUnsent = counts.Pending > 0 || counts.Failed > 0;
    const hasUnsynced = needsReview || hasUnsent || counts.Syncing > 0;
    const records = hasUnsynced ? await this.queueDb.getAllByShopDevice(shopId, settings.deviceId) : [];
    this.unsentSales.set(
      records
        .filter((record) => record.status !== 'Synced' && record.status !== 'SyncedWithWarnings')
        .sort((a, b) => a.soldAt.localeCompare(b.soldAt)),
    );
    this.reviewSales.set(needsReview ? this.toReviewSales(records) : []);
    this.awaitingIssuerSales.set(hasUnsent ? this.toAwaitingIssuerSales(records, session?.user.id ?? null) : []);
    return visible;
  }

  async cleanupSyncedRecords(): Promise<number> {
    return await this.queueDb.deleteOldSyncedRecords();
  }

  private currentUserId(): string | null {
    return this.auth.session()?.user.id ?? null;
  }

  private async syncRecords(
    shopId: string,
    deviceId: string,
    allRecords: readonly OfflineQueuedSaleRecord[],
  ): Promise<OfflineSalesSyncRunResult> {
    // Another user's invoices stay Pending: only their issuer (or an owner's recovery bundle) may submit them.
    const userId = this.currentUserId();
    const records = allRecords.filter((record) => isSubmittableBy(record, userId));
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

  private toAwaitingIssuerSales(
    records: readonly OfflineQueuedSaleRecord[],
    userId: string | null,
  ): readonly OfflineAwaitingIssuerSale[] {
    return records
      .filter((record) => (record.status === 'Pending' || record.status === 'Failed') && !isSubmittableBy(record, userId))
      .sort((a, b) => a.soldAt.localeCompare(b.soldAt))
      .map((record) => ({
        clientSaleId: record.clientSaleId,
        invoiceNumber: record.invoiceNumber,
        soldAt: record.soldAt,
        issuedByUserId: record.payload.issuedByUserId as string,
        status: record.status as 'Pending' | 'Failed',
      }));
  }

  private toReviewSales(records: readonly OfflineQueuedSaleRecord[]): readonly OfflineReviewSale[] {
    return records
      .filter((record) => record.status === 'NeedsReview')
      .sort((a, b) => a.soldAt.localeCompare(b.soldAt))
      .map((record) => ({
        clientSaleId: record.clientSaleId,
        invoiceNumber: record.invoiceNumber,
        soldAt: record.soldAt,
        errorCode: record.errorCode,
        errorMessage: record.errorMessage,
        reviewCaseId: record.reviewCaseId ?? null,
        mismatches: record.mismatches ?? [],
      }));
  }

  private buildLockKey(shopId: string, deviceId: string): string {
    return `${shopId}::${deviceId}`;
  }
}
