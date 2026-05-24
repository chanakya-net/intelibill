import { Injectable, Injector, effect, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { NetworkStatusService } from '../../../core/services/network-status.service';
import { OfflineSalesDeviceSettingsStorage } from '../../../core/storage/offline-sales-device-settings.storage';
import { OfflineQueuedSaleRecord, OfflineQueueSyncResult, OfflineSaleQueueStatus } from './offline-sale-core.types';
import { OfflineSalesQueueIndexedDbService } from './offline-sales-queue-indexeddb.service';
import { SaleService } from './sale.service';
import type { OfflineSaleSyncRequest, OfflineSaleSyncResultDto } from './sale.models';

const SYNC_BATCH_SIZE = 50;

export interface OfflineSalesVisibleQueueCounts {
  readonly pending: number;
  readonly syncing: number;
  readonly failed: number;
  readonly warning: number;
  readonly needsReview: number;
  readonly totalVisible: number;
}

export interface OfflineSalesSyncRunResult {
  readonly attemptedCount: number;
  readonly syncedCount: number;
  readonly warningCount: number;
  readonly needsReviewCount: number;
  readonly failedCount: number;
  readonly skippedReason?: 'NO_ACTIVE_SHOP' | 'NO_DEVICE' | 'API_UNREACHABLE' | 'NOT_RETRYABLE';
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
        if (!record || !this.isRetryable(record.status)) {
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
    const visible = this.toVisibleCounts(counts);
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

    for (let start = 0; start < records.length; start += SYNC_BATCH_SIZE) {
      const batch = records.slice(start, start + SYNC_BATCH_SIZE);
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
          sales: marked.map((record) => this.toSyncRequest(record)),
        }));
        const byClientSaleId = new Map(response.results.map((result) => [result.clientSaleId, result]));

        for (const record of marked) {
          const result = byClientSaleId.get(record.clientSaleId);
          const applied = await this.applyBackendResult(shopId, deviceId, record, result);
          this.addResultToTotals(totals, applied.status);
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
    const mapped = this.toQueueSyncResult(result);
    await this.queueDb.applySyncResult(shopId, deviceId, record.clientSaleId, mapped);
    return mapped;
  }

  private toQueueSyncResult(result: OfflineSaleSyncResultDto | undefined): OfflineQueueSyncResult {
    if (!result) {
      return {
        status: 'Failed',
        errorCode: 'offline_sync.result_missing',
        errorMessage: 'Offline sale sync did not return a result for this sale.',
      };
    }

    const status = this.normalizeStatus(result.status);
    const firstError = result.errors[0];
    return {
      status,
      warnings: result.warnings ?? [],
      errorCode: firstError?.code ?? null,
      errorMessage: firstError?.message ?? null,
      serverSaleId: result.saleId ?? null,
    };
  }

  private normalizeStatus(status: string): OfflineQueueSyncResult['status'] {
    const normalized = status.trim().toLowerCase();

    if (normalized === 'created' || normalized === 'duplicate' || normalized === 'synced') return 'Synced';
    if (normalized === 'syncedwithwarnings') return 'SyncedWithWarnings';
    if (normalized === 'needsreview') return 'NeedsReview';
    if (normalized === 'failed') return 'Failed';

    return 'Failed';
  }

  private toSyncRequest(record: OfflineQueuedSaleRecord): OfflineSaleSyncRequest {
    const payload = record.payload;
    const totals = payload.pricing.totals;
    return {
      clientSaleId: payload.clientSaleId,
      invoiceNumber: payload.invoiceNumber,
      soldAt: payload.soldAt,
      customerId: payload.customerId,
      customerName: payload.customerName,
      customerPhone: payload.customerPhone,
      paymentMethod: payload.paymentMethod,
      paidAmount: totals.paidAmount,
      dueAmount: totals.dueAmount,
      subtotalBeforeDiscount: totals.totalBeforeDiscount,
      totalBeforeDiscount: totals.totalBeforeDiscount,
      totalDiscountAmount: totals.totalDiscount,
      totalTaxAmount: totals.totalTax,
      totalAmount: totals.grandTotal,
      saleDiscountOverrideType: payload.pricing.saleDiscountOverrideType ?? 0,
      saleDiscountOverrideValue: payload.pricing.saleDiscountOverrideValue ?? 0,
      configuredSaleRuleId: payload.pricing.configuredSaleRuleId ?? null,
      configuredSaleRuleType: payload.pricing.configuredSaleRuleType ?? null,
      configuredSaleRulePercentage: payload.pricing.configuredSaleRulePercentage ?? null,
      configuredSaleRuleThresholdAmount: payload.pricing.configuredSaleRuleThresholdAmount ?? null,
      items: payload.pricing.lines.map((line) => ({
        barcode: line.barcode,
        batchNumber: line.batchNumber,
        itemName: line.itemName,
        quantity: line.quantity,
        costPrice: line.costPrice,
        salesPrice: line.salesPrice,
        mrp: line.mrp,
        taxRatePercent: line.taxRatePercent,
        isPriceIncludingTax: line.taxIncluded,
        inventoryBatchId: line.inventoryBatchId,
        preTaxAmountBeforeDiscount: line.preTaxAmount,
        itemDiscountAmount: line.itemDiscountAmount,
        saleDiscountAmount: line.saleDiscountAmount,
        taxableAmount: line.taxableAmount,
        taxAmount: line.taxAmount,
        totalAmount: line.lineTotal,
        configuredBatchRuleId: line.configuredRuleId,
        configuredBatchRulePercentage: line.configuredRulePercentage ?? null,
        itemDiscountOverrideType: line.itemDiscountOverrideType ?? 0,
        itemDiscountOverrideValue: line.itemDiscountOverrideValue ?? 0,
        hsnCode: line.hsnCode,
      })),
    };
  }

  private toVisibleCounts(counts: Record<OfflineSaleQueueStatus, number>): OfflineSalesVisibleQueueCounts {
    const visible = {
      pending: counts.Pending,
      syncing: counts.Syncing,
      failed: counts.Failed,
      warning: counts.SyncedWithWarnings,
      needsReview: counts.NeedsReview,
    };

    return {
      ...visible,
      totalVisible: visible.pending + visible.syncing + visible.failed + visible.warning + visible.needsReview,
    };
  }

  private addResultToTotals(
    totals: { syncedCount: number; warningCount: number; needsReviewCount: number; failedCount: number },
    status: OfflineQueueSyncResult['status'],
  ): void {
    if (status === 'Synced') totals.syncedCount += 1;
    else if (status === 'SyncedWithWarnings') totals.warningCount += 1;
    else if (status === 'NeedsReview') totals.needsReviewCount += 1;
    else totals.failedCount += 1;
  }

  private isRetryable(status: OfflineSaleQueueStatus): boolean {
    return status === 'Pending' || status === 'Failed';
  }

  private buildLockKey(shopId: string, deviceId: string): string {
    return `${shopId}::${deviceId}`;
  }
}
