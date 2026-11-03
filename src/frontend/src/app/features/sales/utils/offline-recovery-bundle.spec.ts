import { createHash } from 'node:crypto';
import { describe, expect, it } from 'vitest';

import type { OfflineQueuedSalePayload, OfflineQueuedSaleRecord, OfflineSaleQueueStatus } from '../services/offline-sale-core.types';
import { buildSyncRequestFromQueuedSale } from './offline-sale-sync.mapper';
import { buildSupportBundle } from './offline-recovery-bundle';

const context = { shopId: 'shop-1', deviceId: 'dev/1', exportedAt: '2026-11-03T09:05:30.000Z' };

function rec(clientSaleId: string, status: OfflineSaleQueueStatus, issuedByUserId?: string): OfflineQueuedSaleRecord {
  const payload: OfflineQueuedSalePayload = {
    clientSaleId,
    idempotencyKey: `idem-${clientSaleId}`,
    shopId: 'shop-1',
    deviceId: 'dev/1',
    invoiceNumber: `INV-${clientSaleId}`,
    soldAt: '2026-11-01T10:00:00.000Z',
    pricing: {
      lines: [],
      totals: { totalBeforeDiscount: 100, totalDiscount: 0, totalTax: 0, grandTotal: 100, paidAmount: 100, dueAmount: 0 },
    },
    paymentMethod: 1,
    customerId: null,
    customerName: null,
    customerPhone: null,
    ...(issuedByUserId ? { issuedByUserId } : {}),
  };
  return {
    key: `k-${clientSaleId}`, shopId: 'shop-1', deviceId: 'dev/1', clientSaleId,
    idempotencyKey: payload.idempotencyKey, invoiceNumber: payload.invoiceNumber, soldAt: payload.soldAt,
    payload, status, warnings: [], errorCode: null, errorMessage: null, serverSaleId: null,
    createdAt: payload.soldAt, updatedAt: payload.soldAt, syncAttemptCount: 0, lastSyncAttemptAt: null, syncAttempts: [],
  };
}

describe('buildSupportBundle', () => {
  const records = [rec('a', 'Pending', 'user-1'), rec('b', 'Failed'), rec('c', 'Syncing'), rec('d', 'NeedsReview', 'user-2'),
    rec('e', 'Synced'), rec('f', 'SyncedWithWarnings')];

  it('checksum is SHA-256 of the invoices text', async () => {
    const bundle = await buildSupportBundle(records, context);
    const parsed = JSON.parse(bundle.text) as { checksum: string; invoices: unknown[] };
    const invoicesText = JSON.stringify(parsed.invoices);

    expect(bundle.text).toContain('"invoices":' + invoicesText + '}');
    expect(bundle.checksum).toBe(createHash('sha256').update(invoicesText, 'utf8').digest('hex'));
    expect(parsed.checksum).toBe(bundle.checksum);
  });

  it('excludes synced records and lists the covered client sale ids', async () => {
    const bundle = await buildSupportBundle(records, context);
    expect(bundle.clientSaleIds).toEqual(['a', 'b', 'c', 'd']);
    expect((JSON.parse(bundle.text).invoices as { clientSaleId: string }[]).map((i) => i.clientSaleId)).toEqual(['a', 'b', 'c', 'd']);
  });

  it('round-trips through JSON.parse with the sync request shape plus issuer', async () => {
    const bundle = await buildSupportBundle(records, context);
    const parsed = JSON.parse(bundle.text);

    expect(parsed).toMatchObject({ format: 'intelibill.offline-recovery', version: 1, shopId: 'shop-1', deviceId: 'dev/1', exportedAt: context.exportedAt });
    expect(Object.keys(parsed).slice(0, 6)).toEqual(['format', 'version', 'shopId', 'deviceId', 'exportedAt', 'checksum']);
    expect(parsed.invoices[0]).toEqual({ ...JSON.parse(JSON.stringify(buildSyncRequestFromQueuedSale(records[0]))), issuedByUserId: 'user-1' });
    expect('issuedByUserId' in parsed.invoices[1]).toBe(false);
  });

  it('contains no token or session fields', async () => {
    const bundle = await buildSupportBundle(records, context);
    expect(bundle.text).not.toMatch(/token|session|refresh|password/i);
  });

  it('names the file after the device and export minute', async () => {
    const bundle = await buildSupportBundle(records, context);
    expect(bundle.fileName).toBe('offline-recovery-dev-1-202611030905.json');
  });
});
