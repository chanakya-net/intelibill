import { Injectable } from '@angular/core';

import { AuthService } from '../../../core/auth/auth.service';
import { SALE_ENDPOINTS } from '../../../core/auth/auth.constants';
import {
  OfflineSalesSnapshotIndexedDbService,
  type OfflineActiveLeaseSnapshot,
  type OfflineCustomerLiteSnapshot,
  type OfflineDiscountRuleSnapshot,
  type OfflineSalesSnapshotMetadata,
  type OfflineSellableBatchSnapshot,
} from '../../../core/storage/offline-sales-snapshot-indexeddb.service';

type OfflineSnapshotStreamRecord =
  | { readonly type: 'metadata'; readonly metadata: OfflineSalesSnapshotMetadata }
  | { readonly type: 'batch'; readonly batch: OfflineSellableBatchSnapshot }
  | { readonly type: 'customer'; readonly customer: OfflineCustomerLiteSnapshot }
  | { readonly type: 'discountRule'; readonly discountRule: OfflineDiscountRuleSnapshot }
  | { readonly type: 'activeLease'; readonly activeLease: OfflineActiveLeaseSnapshot }
  | {
      readonly type: 'complete';
      readonly complete: {
        readonly snapshotId: string;
        readonly completedAt: string;
      };
    }
  | {
      readonly type: 'error';
      readonly error: {
        readonly snapshotId: string;
        readonly code: string;
        readonly message: string;
      };
    };

@Injectable({ providedIn: 'root' })
export class OfflineSalesSnapshotSyncService {
  constructor(
    private readonly authService: AuthService,
    private readonly snapshotDb: OfflineSalesSnapshotIndexedDbService
  ) {}

  async syncForShop(shopId: string): Promise<void> {
    const token = this.authService.getAccessToken();
    if (!token || !shopId) return;

    let snapshotId: string | null = null;

    try {
      const response = await fetch(SALE_ENDPOINTS.offlineSnapshotStream, {
        headers: { Authorization: `Bearer ${token}` },
      });

      if (!response.ok || !response.body) {
        return;
      }

      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      let buffer = '';
      let sawComplete = false;

      while (true) {
        const { done, value } = await reader.read();

        if (done) {
          buffer += decoder.decode();
          break;
        }

        buffer += decoder.decode(value, { stream: true });
        const lines = buffer.split('\n');
        buffer = lines.pop() ?? '';

        for (const line of lines) {
          const trimmed = line.trim();
          if (!trimmed) continue;

          let record: OfflineSnapshotStreamRecord;
          try {
            record = JSON.parse(trimmed) as OfflineSnapshotStreamRecord;
          } catch {
            continue;
          }

          if (record.type === 'metadata') {
            snapshotId = record.metadata.snapshotId;
            await this.snapshotDb.beginAttempt(record.metadata);
            continue;
          }

          if (!snapshotId) {
            continue;
          }

          switch (record.type) {
            case 'batch':
              await this.snapshotDb.writeBatch(snapshotId, shopId, record.batch);
              break;
            case 'customer':
              await this.snapshotDb.writeCustomer(snapshotId, shopId, record.customer);
              break;
            case 'discountRule':
              await this.snapshotDb.writeDiscountRule(snapshotId, shopId, record.discountRule);
              break;
            case 'activeLease':
              await this.snapshotDb.writeActiveLease(snapshotId, shopId, record.activeLease);
              break;
            case 'complete':
              sawComplete = true;
              await this.snapshotDb.markComplete(snapshotId, shopId, record.complete.completedAt);
              break;
            case 'error':
              await this.snapshotDb.markFailed(snapshotId, shopId, record.error.code, record.error.message);
              break;
            default:
              break;
          }

          if (sawComplete) {
            // Complete record is the terminal marker for usability.
            return;
          }
        }
      }

      const remaining = buffer.trim();
      if (remaining) {
        try {
          const record = JSON.parse(remaining) as OfflineSnapshotStreamRecord;
          if (record.type === 'complete' && snapshotId) {
            await this.snapshotDb.markComplete(snapshotId, shopId, record.complete.completedAt);
            return;
          }
        } catch {
          // ignore
        }
      }

      if (snapshotId && !sawComplete) {
        await this.snapshotDb.markFailed(
          snapshotId,
          shopId,
          'OfflineSnapshot.Incomplete',
          'Stream ended before complete record.'
        );
      }
    } catch (error) {
      if (snapshotId) {
        const message = error instanceof Error ? error.message : 'Unknown stream failure.';
        await this.snapshotDb.markFailed(snapshotId, shopId, 'OfflineSnapshot.StreamFailed', message);
      }
    }
  }
}
