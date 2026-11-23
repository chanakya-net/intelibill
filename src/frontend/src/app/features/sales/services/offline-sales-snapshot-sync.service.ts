import { underOfflineAuthorityLock } from '../../../core/storage/offline-issuance-lock';
import { observeShopDataRefusal } from '../../../core/context/shop-data-response';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { isOfflineSalesSnapshotMetadata, hasOfflineExpiryAuthority } from '../utils/offline-sales-snapshot.schema';
import { Injectable, Optional } from '@angular/core';

import { AuthService } from '../../../core/auth/auth.service';
import { SALE_ENDPOINTS } from '../../../core/auth/auth.constants';
import { withServiceWorkerBypassQuery } from '../../../core/service-worker/service-worker-bypass';
import {
  OfflineSalesSnapshotIndexedDbService,
  type OfflineActiveLeaseSnapshot,
  type OfflineCustomerLiteSnapshot,
  type OfflineDiscountRuleSnapshot,
  type OfflineSalesSnapshotMetadata,
  type OfflineSellableBatchSnapshot,
  type OfflineSellableServiceSnapshot,
} from '../../../core/storage/offline-sales-snapshot-indexeddb.service';
import type { OfflineLicencePermitBinding, OfflineSnapshotLicencePermitRecord } from '../utils/offline-licence-permit';
import { OfflineLicencePermitService } from './offline-licence-permit.service';

type OfflineSnapshotStreamRecord =
  | { readonly type: 'metadata'; readonly metadata: OfflineSalesSnapshotMetadata }
  | { readonly type: 'batch'; readonly batch: OfflineSellableBatchSnapshot }
  | { readonly type: 'customer'; readonly customer: OfflineCustomerLiteSnapshot }
  | { readonly type: 'discountRule'; readonly discountRule: OfflineDiscountRuleSnapshot }
  | { readonly type: 'activeLease'; readonly activeLease: OfflineActiveLeaseSnapshot }
  | { readonly type: 'service'; readonly service: OfflineSellableServiceSnapshot }
  | { readonly type: 'licencePermit'; readonly licencePermit: OfflineSnapshotLicencePermitRecord }
  | {
      readonly type: 'complete';
      readonly complete: {
        readonly snapshotId: string;
        readonly completedAt: string;
        readonly batchCount: number; readonly customerCount: number; readonly discountRuleCount: number; readonly activeLeaseCount: number; readonly serviceCount: number;
      };
    }
  | {
      readonly type: 'error';
      readonly error: {
        readonly snapshotId: string;
        readonly code: string;
        readonly message: string;
        readonly traceId?: string;
      };
    };

function buildSnapshotStreamUrl(binding: OfflineLicencePermitBinding | null): string {
  if (!binding) return withServiceWorkerBypassQuery(SALE_ENDPOINTS.offlineSnapshotStream);
  const params = new URLSearchParams({
    leaseId: binding.leaseId,
    grantId: binding.grantId,
    deviceId: binding.deviceId,
  });
  if (binding.tillSessionId) params.set('tillSessionId', binding.tillSessionId);
  return withServiceWorkerBypassQuery(`${SALE_ENDPOINTS.offlineSnapshotStream}?${params.toString()}`);
}

@Injectable({ providedIn: 'root' })
export class OfflineSalesSnapshotSyncService {
  constructor(
    private readonly authService: AuthService,
    private readonly snapshotDb: OfflineSalesSnapshotIndexedDbService,
    private readonly context: ShopContextService,
    @Optional() private readonly licencePermit?: OfflineLicencePermitService,
  ) {}

  async syncForShop(shopId: string, explicitBinding?: OfflineLicencePermitBinding | null): Promise<void> {
    const token = this.authService.getAccessToken();
    if (!token || !shopId) return;

    let snapshotId: string | null = null;
    let authority = false;
    let catalogVersion = 0;
    let pendingPermitRecord: OfflineSnapshotLicencePermitRecord | null = null;
    const invocation = this.context.scope();
    if (!invocation || invocation.shopId !== shopId || this.context.dataRestricted()) return;
    const epoch = this.context.epoch();
    const binding =
      explicitBinding === undefined ? ((await this.licencePermit?.resolveBinding(shopId)) ?? null) : explicitBinding;
    const abort = new AbortController();
    const unregister = this.context.onDetach(() => abort.abort());
    const ensureScope = () => {
      if (this.context.epoch() !== epoch || !this.context.isCurrent(invocation) || this.context.dataRestricted())
        throw new Error('Snapshot scope changed.');
    };
    const received = { batchCount: 0, customerCount: 0, discountRuleCount: 0, activeLeaseCount: 0, serviceCount: 0 };
    const seen = new Set<string>();
    const promote = async (value: Extract<OfflineSnapshotStreamRecord, { type: 'complete' }>['complete']) => {
      ensureScope();
      if (!authority || value.snapshotId !== snapshotId || Object.entries(received).some(([key, count]) => value[key as keyof typeof received] !== count)) throw new Error('Incomplete snapshot record counts.');
      await underOfflineAuthorityLock(shopId, async () => {
        ensureScope();
        await this.snapshotDb.markComplete(value.snapshotId, shopId, value.completedAt, abort.signal);
        if (!binding || !pendingPermitRecord || !this.licencePermit) return;
        try {
          ensureScope();
          await this.licencePermit.acceptDeliveredPermitUnderLock(binding, pendingPermitRecord, value.snapshotId, value.completedAt);
        } catch {
          // The snapshot remains usable if permit storage or verification fails.
        }
      });
    };

    try {
      const response = await fetch(buildSnapshotStreamUrl(binding), {
        cache: 'no-store',
        signal: abort.signal,
        headers: { Authorization: `Bearer ${token}` },
      });

      ensureScope();
      if (!response.ok) {
        await observeShopDataRefusal(response, this.context, invocation);
        return;
      }
      if (!response.body) return;

      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      let buffer = '';
      let sawComplete = false;

      while (true) {
        const { done, value } = await reader.read();
        ensureScope();

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

          ensureScope();
          let record: OfflineSnapshotStreamRecord;
          try {
            record = JSON.parse(trimmed) as OfflineSnapshotStreamRecord;
          } catch { throw new Error('Malformed snapshot record.'); }

          if (record.type === 'metadata') {
            if (snapshotId) throw new Error('Duplicate snapshot metadata.');
            snapshotId = record.metadata.snapshotId;
            catalogVersion = record.metadata.catalogVersion ?? 0;
            authority = record.metadata.shopId === shopId && record.metadata.schemaVersion === 4 && isOfflineSalesSnapshotMetadata(record.metadata);
            await this.snapshotDb.beginAttempt(record.metadata);
            if (!authority) {
              await this.snapshotDb.markFailed(snapshotId, shopId, 'Expiry.RefreshRequired', 'Refresh the expiry catalog online.');
              return;
            }
            continue;
          }

          if (!snapshotId) {
            continue;
          }

          const entityId = record.type === 'batch' ? record.batch.batchId : record.type === 'customer' ? record.customer.customerId : record.type === 'discountRule' ? record.discountRule.ruleId : record.type === 'activeLease' ? record.activeLease.leaseId : record.type === 'service' ? record.service.serviceId : null;
          if (!['complete', 'error', 'licencePermit'].includes(record.type) && !entityId) throw new Error('Missing snapshot entity identity.');
          if (entityId) { const key = `${record.type}:${entityId}`; if (seen.has(key)) throw new Error('Duplicate snapshot entity.'); seen.add(key); }
          switch (record.type) {
            case 'batch':
              if (!hasOfflineExpiryAuthority(record.batch, catalogVersion)) throw new Error('Incomplete lot authority.');
              received.batchCount++;
              await this.snapshotDb.writeBatch(snapshotId, shopId, record.batch);
              break;
            case 'customer':
              received.customerCount++;
              await this.snapshotDb.writeCustomer(snapshotId, shopId, record.customer);
              break;
            case 'discountRule':
              received.discountRuleCount++;
              await this.snapshotDb.writeDiscountRule(snapshotId, shopId, record.discountRule);
              break;
            case 'activeLease':
              received.activeLeaseCount++;
              await this.snapshotDb.writeActiveLease(snapshotId, shopId, record.activeLease);
              break;
            case 'service':
              received.serviceCount++;
              await this.snapshotDb.writeService(snapshotId, shopId, record.service);
              break;
            case 'licencePermit':
              pendingPermitRecord = record.licencePermit;
              break;
            case 'complete':
              await promote(record.complete);
              sawComplete = true;
              break;
            case 'error':
              // Terminal, like complete: the server stopped mid-stream, so nothing after this
              // line may make the attempt usable. The trace id is what support looks it up by.
              await this.snapshotDb.markFailed(
                snapshotId,
                shopId,
                record.error.code,
                record.error.message,
                record.error.traceId
              );
              return;
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
          if (record.type === 'complete' && snapshotId && authority && record.complete.snapshotId === snapshotId) {
            await promote(record.complete);
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
    } catch {
      if (snapshotId) {
        const message = 'Snapshot stream could not be verified. Refresh online.';
        await this.snapshotDb.markFailed(snapshotId, shopId, 'OfflineSnapshot.StreamFailed', message);
      }
    } finally {
      unregister();
      abort.abort();
    }
  }
}
