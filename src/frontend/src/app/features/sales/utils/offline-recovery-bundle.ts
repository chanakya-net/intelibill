import type { OfflineQueuedSaleRecord, OfflineSaleQueueStatus } from '../services/offline-sale-core.types';
import { buildSyncRequestFromQueuedSale } from './offline-sale-sync.mapper';

export const OFFLINE_RECOVERY_BUNDLE_FORMAT = 'intelibill.offline-recovery';
export const OFFLINE_RECOVERY_BUNDLE_VERSION = 1;

/**
 * What one bundle file may hold so the server accepts it: at most 100 invoices (it answers `BundleTooLarge`
 * above that) and comfortably under its 2 MiB request limit once wrapped in the submission body.
 */
export const SUPPORT_BUNDLE_LIMITS: SupportBundleLimits = { maxInvoices: 100, maxBytes: 1_900_000 };

const EXPORTABLE_STATUSES: readonly OfflineSaleQueueStatus[] = ['Pending', 'Syncing', 'Failed', 'NeedsReview'];

export interface SupportBundleContext {
  readonly shopId: string;
  readonly deviceId: string;
  readonly exportedAt: string;
}

export interface SupportBundleLimits {
  readonly maxInvoices: number;
  /** UTF-8 bytes of the whole file. */
  readonly maxBytes: number;
}

/** One complete bundle file: its own checksum, importable on its own. */
export interface SupportBundle {
  readonly fileName: string;
  readonly text: string;
  readonly checksum: string;
  readonly clientSaleIds: readonly string[];
}

/**
 * Exports every invoice the server has not yet received, as one or more complete bundle files that each fit what
 * the server accepts. Each file's `checksum` is the lowercase hex SHA-256 of the exact text of its own `invoices`
 * array, which the server re-reads byte-for-byte. A single file keeps the plain name; several are named
 * `…-part<N>-of-<M>.json`. Nothing is exported (an empty list) when every invoice has reached the server. The
 * bundles carry no session data.
 */
export async function buildSupportBundle(
  records: readonly OfflineQueuedSaleRecord[],
  context: SupportBundleContext,
  limits: SupportBundleLimits = SUPPORT_BUNDLE_LIMITS,
): Promise<readonly SupportBundle[]> {
  const exportable = records.filter((record) => EXPORTABLE_STATUSES.includes(record.status));
  const invoices = exportable.map((record) => ({
    clientSaleId: record.clientSaleId,
    // Each element exactly as JSON.stringify writes it inside an array, so joined texts equal the array's text.
    text: JSON.stringify({
      ...buildSyncRequestFromQueuedSale(record),
      ...(record.payload.issuedByUserId ? { issuedByUserId: record.payload.issuedByUserId } : {}),
    }),
  }));

  // The envelope around the invoices array is the same size for every part: the checksum is always 64 hex digits.
  const envelopeBytes = utf8Length(wrap(context, '0'.repeat(64), ''));
  const groups: (typeof invoices)[] = [];
  let current: typeof invoices = [];
  let currentBytes = 0;
  for (const invoice of invoices) {
    const invoiceBytes = utf8Length(invoice.text);
    // '[' and ']' plus a comma before every element but the first.
    const wouldBe = envelopeBytes + 2 + currentBytes + (current.length > 0 ? 1 : 0) + invoiceBytes;
    if (current.length > 0 && (current.length >= limits.maxInvoices || wouldBe > limits.maxBytes)) {
      groups.push(current);
      current = [];
      currentBytes = 0;
    }
    currentBytes += (current.length > 0 ? 1 : 0) + invoiceBytes;
    current.push(invoice);
  }
  if (current.length > 0) groups.push(current);

  const baseName = `offline-recovery-${safeFileSegment(context.deviceId)}-${formatMinute(context.exportedAt)}`;
  return await Promise.all(
    groups.map(async (group, index) => {
      const invoicesText = '[' + group.map((invoice) => invoice.text).join(',') + ']';
      const checksum = await sha256Hex(invoicesText);
      return {
        fileName: groups.length === 1 ? `${baseName}.json` : `${baseName}-part${index + 1}-of-${groups.length}.json`,
        text: wrap(context, checksum, invoicesText),
        checksum,
        clientSaleIds: group.map((invoice) => invoice.clientSaleId),
      };
    }),
  );
}

function wrap(context: SupportBundleContext, checksum: string, invoicesText: string): string {
  return (
    '{"format":' + JSON.stringify(OFFLINE_RECOVERY_BUNDLE_FORMAT) +
    ',"version":' + OFFLINE_RECOVERY_BUNDLE_VERSION +
    ',"shopId":' + JSON.stringify(context.shopId) +
    ',"deviceId":' + JSON.stringify(context.deviceId) +
    ',"exportedAt":' + JSON.stringify(context.exportedAt) +
    ',"checksum":"' + checksum +
    '","invoices":' + invoicesText + '}'
  );
}

function utf8Length(value: string): number {
  return new TextEncoder().encode(value).length;
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
