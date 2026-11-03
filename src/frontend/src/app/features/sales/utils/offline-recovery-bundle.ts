import type { OfflineQueuedSaleRecord, OfflineSaleQueueStatus } from '../services/offline-sale-core.types';
import { buildSyncRequestFromQueuedSale } from './offline-sale-sync.mapper';

export const OFFLINE_RECOVERY_BUNDLE_FORMAT = 'intelibill.offline-recovery';
export const OFFLINE_RECOVERY_BUNDLE_VERSION = 1;

const EXPORTABLE_STATUSES: readonly OfflineSaleQueueStatus[] = ['Pending', 'Syncing', 'Failed', 'NeedsReview'];

export interface SupportBundleContext {
  readonly shopId: string;
  readonly deviceId: string;
  readonly exportedAt: string;
}

export interface SupportBundle {
  readonly fileName: string;
  readonly text: string;
  readonly checksum: string;
  readonly clientSaleIds: readonly string[];
}

/**
 * Exports every invoice the server has not yet received. `checksum` is the lowercase hex SHA-256 of the exact
 * text of the `invoices` array, which the server re-reads byte-for-byte. The bundle carries no session data.
 */
export async function buildSupportBundle(
  records: readonly OfflineQueuedSaleRecord[],
  context: SupportBundleContext,
): Promise<SupportBundle> {
  const exportable = records.filter((record) => EXPORTABLE_STATUSES.includes(record.status));
  const invoices = exportable.map((record) => ({
    ...buildSyncRequestFromQueuedSale(record),
    ...(record.payload.issuedByUserId ? { issuedByUserId: record.payload.issuedByUserId } : {}),
  }));
  const invoicesText = JSON.stringify(invoices);
  const checksum = await sha256Hex(invoicesText);

  const text =
    '{"format":' + JSON.stringify(OFFLINE_RECOVERY_BUNDLE_FORMAT) +
    ',"version":' + OFFLINE_RECOVERY_BUNDLE_VERSION +
    ',"shopId":' + JSON.stringify(context.shopId) +
    ',"deviceId":' + JSON.stringify(context.deviceId) +
    ',"exportedAt":' + JSON.stringify(context.exportedAt) +
    ',"checksum":"' + checksum +
    '","invoices":' + invoicesText + '}';

  return {
    fileName: `offline-recovery-${safeFileSegment(context.deviceId)}-${formatMinute(context.exportedAt)}.json`,
    text,
    checksum,
    clientSaleIds: exportable.map((record) => record.clientSaleId),
  };
}

async function sha256Hex(value: string): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(value));
  return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, '0')).join('');
}

function safeFileSegment(value: string): string {
  return value.replace(/[^A-Za-z0-9_-]/g, '-');
}

/** UTC, so the same export always gets the same name. */
function formatMinute(iso: string): string {
  const date = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getUTCFullYear()}${pad(date.getUTCMonth() + 1)}${pad(date.getUTCDate())}${pad(date.getUTCHours())}${pad(date.getUTCMinutes())}`;
}
