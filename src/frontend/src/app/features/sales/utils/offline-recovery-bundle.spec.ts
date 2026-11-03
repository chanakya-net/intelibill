import { createHash } from 'node:crypto';
import { describe, expect, it } from 'vitest';

import type { OfflineQueuedSalePayload, OfflineQueuedSaleRecord, OfflineSaleQueueStatus } from '../services/offline-sale-core.types';
import { buildSyncRequestFromQueuedSale } from './offline-sale-sync.mapper';
import { buildSupportBundle, type SupportBundle } from './offline-recovery-bundle';

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

  async function single(input = records): Promise<SupportBundle> {
    const parts = await buildSupportBundle(input, context);
    expect(parts).toHaveLength(1);
    return parts[0];
  }

  function checksumOf(part: SupportBundle): string {
    const parsed = JSON.parse(part.text) as { invoices: unknown[] };
    return createHash('sha256').update(JSON.stringify(parsed.invoices), 'utf8').digest('hex');
  }

  it('checksum is SHA-256 of the invoices text', async () => {
    const bundle = await single();
    const parsed = JSON.parse(bundle.text) as { checksum: string; invoices: unknown[] };
    const invoicesText = JSON.stringify(parsed.invoices);

    expect(bundle.text).toContain('"invoices":' + invoicesText + '}');
    expect(bundle.checksum).toBe(createHash('sha256').update(invoicesText, 'utf8').digest('hex'));
    expect(parsed.checksum).toBe(bundle.checksum);
  });

  it('excludes synced records and lists the covered client sale ids', async () => {
    const bundle = await single();
    expect(bundle.clientSaleIds).toEqual(['a', 'b', 'c', 'd']);
    expect((JSON.parse(bundle.text).invoices as { clientSaleId: string }[]).map((i) => i.clientSaleId)).toEqual(['a', 'b', 'c', 'd']);
  });

  it('round-trips through JSON.parse with the sync request shape plus issuer', async () => {
    const bundle = await single();
    const parsed = JSON.parse(bundle.text);

    expect(parsed).toMatchObject({ format: 'intelibill.offline-recovery', version: 1, shopId: 'shop-1', deviceId: 'dev/1', exportedAt: context.exportedAt });
    expect(Object.keys(parsed).slice(0, 6)).toEqual(['format', 'version', 'shopId', 'deviceId', 'exportedAt', 'checksum']);
    expect(parsed.invoices[0]).toEqual({ ...JSON.parse(JSON.stringify(buildSyncRequestFromQueuedSale(records[0]))), issuedByUserId: 'user-1' });
    expect('issuedByUserId' in parsed.invoices[1]).toBe(false);
  });

  it('contains no token or session fields', async () => {
    const bundle = await single();
    expect(bundle.text).not.toMatch(/token|session|refresh|password/i);
  });

  it('names a single file after the device and export minute', async () => {
    const bundle = await single();
    expect(bundle.fileName).toBe('offline-recovery-dev-1-202611030905.json');
  });

  it('exports nothing when every invoice reached the server', async () => {
    expect(await buildSupportBundle([rec('e', 'Synced')], context)).toEqual([]);
  });

  it('splits 101 invoices into two complete parts of at most 100', async () => {
    const many = Array.from({ length: 101 }, (_, i) => rec(`s-${String(i).padStart(3, '0')}`, 'Pending'));

    const parts = await buildSupportBundle(many, context);

    expect(parts.map((part) => part.clientSaleIds.length)).toEqual([100, 1]);
    expect(parts.map((part) => part.fileName)).toEqual([
      'offline-recovery-dev-1-202611030905-part1-of-2.json',
      'offline-recovery-dev-1-202611030905-part2-of-2.json',
    ]);
    expect(parts.flatMap((part) => part.clientSaleIds)).toEqual(many.map((record) => record.clientSaleId));
    for (const part of parts) {
      const parsed = JSON.parse(part.text) as { checksum: string; invoices: { clientSaleId: string }[]; shopId: string; deviceId: string };
      expect(parsed.checksum).toBe(part.checksum);
      expect(checksumOf(part)).toBe(part.checksum);
      expect(parsed.invoices.map((invoice) => invoice.clientSaleId)).toEqual(part.clientSaleIds);
      expect(parsed).toMatchObject({ shopId: 'shop-1', deviceId: 'dev/1' });
    }
  });

  it('splits by size so no part exceeds the byte cap, counting UTF-8 bytes', async () => {
    // Multi-byte names: a cap counted in UTF-16 characters would let these parts run over.
    const heavy = Array.from({ length: 6 }, (_, i) => {
      const base = rec(`h-${i}`, 'Pending');
      return { ...base, payload: { ...base.payload, customerName: 'क'.repeat(300) } };
    });
    const oneInvoiceBytes = new TextEncoder().encode(JSON.stringify(buildSyncRequestFromQueuedSale(heavy[0]))).length;
    const maxBytes = 600 + oneInvoiceBytes * 2;

    const parts = await buildSupportBundle(heavy, context, { maxInvoices: 100, maxBytes });

    expect(parts.length).toBeGreaterThan(1);
    expect(parts.flatMap((part) => part.clientSaleIds)).toEqual(heavy.map((record) => record.clientSaleId));
    for (const part of parts) {
      expect(new TextEncoder().encode(part.text).length).toBeLessThanOrEqual(maxBytes);
      expect(checksumOf(part)).toBe(part.checksum);
    }
  });

  it('keeps the default parts under the server request limit', async () => {
    const many = Array.from({ length: 250 }, (_, i) => rec(`d-${i}`, 'Failed'));

    const parts = await buildSupportBundle(many, context);

    expect(parts).toHaveLength(3);
    for (const part of parts) {
      expect(part.clientSaleIds.length).toBeLessThanOrEqual(100);
      expect(new TextEncoder().encode(part.text).length).toBeLessThanOrEqual(1_900_000);
    }
  });
});
