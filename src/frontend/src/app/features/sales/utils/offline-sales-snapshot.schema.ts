export interface OfflineSalesSnapshotMetadata {
  readonly snapshotId: string;
  readonly shopId: string;
  readonly schemaVersion: number;
  readonly startedAt: string;
}

export type OfflineSalesSnapshotAttemptStatus = 'incomplete' | 'complete' | 'failed';

export interface OfflineSalesSnapshotAttemptRecord {
  readonly snapshotId: string;
  readonly shopId: string;
  readonly schemaVersion: number;
  readonly startedAt: string;
  readonly completedAt?: string;
  readonly status: OfflineSalesSnapshotAttemptStatus;
  readonly errorCode?: string;
  readonly errorMessage?: string;
}

export interface OfflineSellableBatchSnapshot {
  readonly batchId: string;
  readonly itemId: string;
  readonly itemName: string;
  readonly barcode: string;
  readonly uom: string;
  readonly hsnCode?: string | null;
  readonly batchNumber: string;
  readonly quantity: number;
  readonly costPrice: number;
  readonly mrp: number;
  readonly salesPrice: number;
  readonly taxRatePercent: number;
  readonly taxIncluded: boolean;
  readonly purchaseTaxIncluded: boolean;
  readonly expiryDate?: string | null;
}

export interface OfflineCustomerLiteSnapshot {
  readonly customerId: string;
  readonly name: string;
  readonly phoneNumber: string;
}

export interface OfflineDiscountRuleSnapshot {
  readonly ruleId: string;
  readonly ruleType: string;
  readonly name: string;
  readonly description?: string | null;
  readonly inventoryBatchId?: string | null;
  readonly percentage: number;
  readonly thresholdAmount?: number | null;
  readonly startsAt?: string | null;
  readonly endsAt?: string | null;
  readonly belowCostConfirmed: boolean;
}

export interface OfflineActiveLeaseSnapshot {
  readonly leaseId: string;
  readonly invoiceSequenceId: string;
  readonly deviceId: string;
  readonly fiscalYearStart: number;
  readonly prefix: string;
  readonly rangeStart: number;
  readonly rangeEnd: number;
  readonly nextNumber: number;
  readonly numberPadding: number;
  readonly reservedAt: string;
  readonly expiresAt: string;
}

export interface OfflineUsableSnapshotInfo {
  readonly snapshotId: string;
  readonly completedAt: string;
}

export interface OfflineSalesSnapshotShopPointerRecord {
  readonly shopId: string;
  readonly usableSnapshotId?: string;
  readonly usableCompletedAt?: string;
  readonly lastAttemptSnapshotId?: string;
  readonly lastAttemptStartedAt?: string;
  readonly lastAttemptStatus?: OfflineSalesSnapshotAttemptStatus;
}

export interface OfflineSalesSnapshotScopedRecord<T> {
  readonly key: string;
  readonly snapshotId: string;
  readonly shopId: string;
  readonly entityId: string;
  readonly entity: T;
  readonly writtenAt: string;
}

export const OFFLINE_SALES_SNAPSHOT_DATABASE_NAME = 'intelibill-offline-sales-snapshot';
export const OFFLINE_SALES_SNAPSHOT_DATABASE_VERSION = 1;
export const OFFLINE_SALES_SNAPSHOT_ATTEMPTS_STORE = 'snapshot-attempts';
export const OFFLINE_SALES_SNAPSHOT_SHOP_POINTERS_STORE = 'shop-pointers';
export const OFFLINE_SALES_SNAPSHOT_BATCHES_STORE = 'batches';
export const OFFLINE_SALES_SNAPSHOT_CUSTOMERS_STORE = 'customers';
export const OFFLINE_SALES_SNAPSHOT_DISCOUNT_RULES_STORE = 'discount-rules';
export const OFFLINE_SALES_SNAPSHOT_ACTIVE_LEASES_STORE = 'active-leases';

export const OFFLINE_SALES_SNAPSHOT_STORE_CONFIGS = [
  { name: OFFLINE_SALES_SNAPSHOT_ATTEMPTS_STORE, keyPath: 'snapshotId' },
  { name: OFFLINE_SALES_SNAPSHOT_SHOP_POINTERS_STORE, keyPath: 'shopId' },
  { name: OFFLINE_SALES_SNAPSHOT_BATCHES_STORE, keyPath: 'key' },
  { name: OFFLINE_SALES_SNAPSHOT_CUSTOMERS_STORE, keyPath: 'key' },
  { name: OFFLINE_SALES_SNAPSHOT_DISCOUNT_RULES_STORE, keyPath: 'key' },
  { name: OFFLINE_SALES_SNAPSHOT_ACTIVE_LEASES_STORE, keyPath: 'key' },
] as const;

export const OFFLINE_SALES_SNAPSHOT_ATTEMPT_STATUSES = ['incomplete', 'complete', 'failed'] as const;

function isObjectRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function isString(value: unknown): value is string {
  return typeof value === 'string';
}

function isNonEmptyString(value: unknown): value is string {
  return isString(value) && value.trim().length > 0;
}

function hasStatus(value: unknown, statuses: readonly string[]): value is string {
  return isString(value) && statuses.includes(value);
}

export function isOfflineSalesSnapshotMetadata(value: unknown): value is OfflineSalesSnapshotMetadata {
  if (!isObjectRecord(value)) return false;
  return isNonEmptyString(value['snapshotId'])
    && isNonEmptyString(value['shopId'])
    && Number.isFinite(value['schemaVersion'] as number)
    && isString(value['startedAt']);
}

export function isOfflineSalesSnapshotAttemptStatus(value: unknown): value is OfflineSalesSnapshotAttemptStatus {
  return hasStatus(value, OFFLINE_SALES_SNAPSHOT_ATTEMPT_STATUSES);
}

export function isOfflineSalesSnapshotAttemptRecord(value: unknown): value is OfflineSalesSnapshotAttemptRecord {
  if (!isObjectRecord(value)) return false;
  return isOfflineSalesSnapshotMetadata(value)
    && isOfflineSalesSnapshotAttemptStatus(value['status'])
    && isString(value['snapshotId'])
    && isString(value['shopId']);
}

export function isOfflineSalesSnapshotShopPointerRecord(
  value: unknown,
): value is OfflineSalesSnapshotShopPointerRecord {
  return isObjectRecord(value) && isNonEmptyString(value['shopId']);
}

export function configureOfflineSalesSnapshotSchema(database: IDBDatabase): void {
  for (const store of OFFLINE_SALES_SNAPSHOT_STORE_CONFIGS) {
    if (!database.objectStoreNames.contains(store.name)) {
      database.createObjectStore(store.name, { keyPath: store.keyPath });
    }
  }
}
