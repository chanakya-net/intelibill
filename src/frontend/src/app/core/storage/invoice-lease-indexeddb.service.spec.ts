import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import {
  InvoiceLeaseExhaustedError,
  InvoiceLeaseExpiredError,
  InvoiceLeaseIndexedDbService,
  INVOICE_LEASE_RENEWAL_THRESHOLD,
  type InvoiceLeaseSnapshot,
} from './invoice-lease-indexeddb.service';

describe('InvoiceLeaseIndexedDbService', () => {
  let service: InvoiceLeaseIndexedDbService;

  beforeEach(() => {
    service = new InvoiceLeaseIndexedDbService();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('saves and loads a lease scoped to shop/device/fiscal year', async () => {
    stubIndexedDb();
    const lease = makeLease();

    await service.saveLease(lease);

    const loaded = await service.loadLease('shop-1', 'device-1', '2025-26');
    expect(loaded).not.toBeNull();
    expect(loaded!.leaseId).toBe('lease-1');
    expect(loaded!.remainingCount).toBe(2);
  });

  it('consumes the next invoice number atomically', async () => {
    stubIndexedDb();
    const lease = makeLease();
    await service.saveLease(lease);

    const result = await service.consumeNextInvoiceNumber('shop-1', 'device-1', '2025-26');

    expect(result.invoiceNumber).toBe('INV-2025-26-0001');
    expect(result.lease.nextNumber).toBe(2);
    expect(result.remainingCount).toBe(1);
  });

  it('rolls back the consumed invoice only when the lease cursor still matches', async () => {
    stubIndexedDb();
    await service.saveLease(makeLease());

    const result = await service.consumeNextInvoiceNumber('shop-1', 'device-1', '2025-26');
    const restored = await service.rollbackConsumedInvoiceNumber('shop-1', 'device-1', '2025-26', result.lease.nextNumber);

    expect(restored!.nextNumber).toBe(1);
    const loaded = await service.loadLease('shop-1', 'device-1', '2025-26');
    expect(loaded!.nextNumber).toBe(1);
    expect(loaded!.remainingCount).toBe(2);
  });

  it('does not roll back when another consumption has advanced the cursor', async () => {
    stubIndexedDb();
    await service.saveLease(makeLease({ rangeEnd: 3, remainingCount: 3 }));

    const first = await service.consumeNextInvoiceNumber('shop-1', 'device-1', '2025-26');
    await service.consumeNextInvoiceNumber('shop-1', 'device-1', '2025-26');
    const restored = await service.rollbackConsumedInvoiceNumber('shop-1', 'device-1', '2025-26', first.lease.nextNumber);

    expect(restored!.nextNumber).toBe(3);
    const loaded = await service.loadLease('shop-1', 'device-1', '2025-26');
    expect(loaded!.nextNumber).toBe(3);
  });

  it('throws when the lease is expired', async () => {
    stubIndexedDb();
    const lease = makeLease({ expiresAt: new Date(Date.now() - 60_000).toISOString() });
    await service.saveLease(lease);

    await expect(service.consumeNextInvoiceNumber('shop-1', 'device-1', '2025-26')).rejects.toBeInstanceOf(
      InvoiceLeaseExpiredError
    );
  });

  it('throws when the lease is exhausted', async () => {
    stubIndexedDb();
    const lease = makeLease({ nextNumber: 3 });
    await service.saveLease(lease);

    await expect(service.consumeNextInvoiceNumber('shop-1', 'device-1', '2025-26')).rejects.toBeInstanceOf(
      InvoiceLeaseExhaustedError
    );
  });

  it('isolates leases by shop and device', async () => {
    stubIndexedDb();
    await service.saveLease(makeLease());

    const missing = await service.loadLease('shop-2', 'device-1', '2025-26');

    expect(missing).toBeNull();
  });

  it('computes remaining count and renewal threshold', () => {
    const lease = makeLease({ nextNumber: 2 });

    expect(service.getRemainingCount(lease)).toBe(1);
    expect(service.isBelowRenewalThreshold(lease, INVOICE_LEASE_RENEWAL_THRESHOLD)).toBe(true);
  });

  it('attaches a permit only when leaseId and grant match and keeps the later issuedAt', async () => {
    stubIndexedDb();
    const grantSha256 = 'a'.repeat(64);
    await service.saveLease(
      makeLease({
        licenceEvidence: { version: 1, grantId: 'grant-1', grantSha256, keyId: 'key-1' },
      }),
    );

    const basePermit = {
      version: 1 as const,
      permitId: 'permit-1',
      grantId: 'grant-1',
      grantSha256,
      leaseId: 'lease-1',
      snapshotId: 'snap-1',
      keyId: 'key-1',
      signedPermit: 'h.p.s',
      shopId: 'shop-1',
      userId: 'user-1',
      sessionId: 'session-1',
      deviceId: 'device-1',
      authorityVerifiedAt: '2026-11-20T12:00:00.000000Z',
      deviceVerifiedAt: '2026-11-22T11:59:00.000000Z',
      snapshotCompletedAt: '2026-11-22T11:59:30.000000Z',
      issuedAt: '2026-11-22T11:59:30.000000Z',
      notBefore: '2026-11-22T11:59:30.000000Z',
      validUntil: '2026-11-22T12:00:00.000000Z',
      limitingReason: 'AuthorityFreshness',
      tillSessionId: null,
    };

    const first = await service.attachPermit('shop-1', 'device-1', '2025-26', basePermit);
    expect(first).toEqual(basePermit);

    // A permit for another lease or grant is refused without overwriting the stored permit.
    expect(await service.attachPermit('shop-1', 'device-1', '2025-26', { ...basePermit, leaseId: 'lease-other' })).toBeNull();
    expect(await service.attachPermit('shop-1', 'device-1', '2025-26', { ...basePermit, grantId: 'grant-other' })).toBeNull();

    // An older issuedAt never replaces a newer stored permit.
    const older = await service.attachPermit('shop-1', 'device-1', '2025-26', {
      ...basePermit,
      permitId: 'permit-old',
      issuedAt: '2026-11-22T11:59:10.000000Z',
    });
    expect(older).toBeNull();

    // A later issuedAt updates the stored permit.
    const newerPermit = { ...basePermit, permitId: 'permit-2', issuedAt: '2026-11-22T11:59:45.000000Z' };
    expect(await service.attachPermit('shop-1', 'device-1', '2025-26', newerPermit)).toEqual(newerPermit);
    expect((await service.loadLease('shop-1', 'device-1', '2025-26'))?.offlinePermit).toEqual(newerPermit);

    // Saving a fresh lease drops the previous lease's permit in the same write.
    await service.saveLease(makeLease({ leaseId: 'lease-2' }));
    expect((await service.loadLease('shop-1', 'device-1', '2025-26'))?.offlinePermit).toBeUndefined();
  });

  function makeLease(overrides: Partial<InvoiceLeaseSnapshot> = {}): InvoiceLeaseSnapshot {
    const now = Date.now();
    return {
      leaseId: 'lease-1',
      shopId: 'shop-1',
      deviceId: 'device-1',
      fiscalYear: '2025-26',
      prefix: 'INV-2025-26-',
      numberPadding: 4,
      rangeStart: 1,
      rangeEnd: 2,
      nextNumber: 1,
      remainingCount: 2,
      reservedAt: new Date(now).toISOString(),
      expiresAt: new Date(now + 7 * 24 * 60 * 60 * 1000).toISOString(),
      ...overrides,
    };
  }

  function stubIndexedDb(): void {
    const store = new Map<string, unknown>();

    const database = {
      objectStoreNames: {
        contains: vi.fn(() => true),
      },
      createObjectStore: vi.fn(() => undefined),
      transaction: vi.fn((_storeName: string, _mode: IDBTransactionMode) => ({
        objectStore: () => ({
          get: (key: string) => createRequest(() => store.get(key)),
          put: (value: { key: string }) => createRequest(() => {
            store.set(value.key, value);
            return value;
          }),
        }),
      })),
    };

    const openRequest = {
      result: database as unknown as IDBDatabase,
      error: null,
      onupgradeneeded: null as null | (() => void),
      onsuccess: null as null | (() => void),
      onerror: null as null | (() => void),
    };

    vi.stubGlobal('indexedDB', {
      open: vi.fn(() => {
        queueMicrotask(() => openRequest.onupgradeneeded?.());
        queueMicrotask(() => openRequest.onsuccess?.());
        return openRequest;
      }),
    });
  }

  function createRequest<T>(resolveValue: () => T) {
    const request = {
      result: undefined as T | undefined,
      error: null as unknown,
      onsuccess: null as null | (() => void),
      onerror: null as null | (() => void),
    };

    queueMicrotask(() => {
      try {
        request.result = resolveValue();
        request.onsuccess?.();
      } catch (error) {
        request.error = error;
        request.onerror?.();
      }
    });

    return request;
  }
});
