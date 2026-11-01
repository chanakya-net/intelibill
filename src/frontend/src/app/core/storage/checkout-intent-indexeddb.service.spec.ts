import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { CheckoutIntent, CheckoutIntentIndexedDbService } from './checkout-intent-indexeddb.service';

describe('CheckoutIntentIndexedDbService', () => {
  let service: CheckoutIntentIndexedDbService;

  beforeEach(() => {
    service = new CheckoutIntentIndexedDbService();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('resolves add only after the transaction commits', async () => {
    const db = stubIndexedDb();
    let resolved = false;

    const pending = service.add(makeIntent('intent-1')).then(() => (resolved = true));
    await flush();
    expect(db.records.has('intent-1')).toBe(true);
    expect(resolved).toBe(false);

    db.completeAll();
    await pending;
    expect(resolved).toBe(true);
  });

  it('rejects add when the transaction fails', async () => {
    const db = stubIndexedDb();

    const pending = service.add(makeIntent('intent-1'));
    await flush();
    db.failAll(new Error('QuotaExceededError'));

    await expect(pending).rejects.toThrow('QuotaExceededError');
  });

  it('rejects when IndexedDB is unavailable', async () => {
    vi.stubGlobal('indexedDB', undefined);

    await expect(service.add(makeIntent('intent-1'))).rejects.toThrow();
  });

  it('applies an update only when the precondition accepts the current record', async () => {
    const db = stubIndexedDb();
    db.records.set('intent-1', makeIntent('intent-1'));

    const declined = service.update('intent-1', () => null);
    await flush();
    db.completeAll();
    expect(await declined).toBeNull();
    expect(db.records.get('intent-1')?.status).toBe('prepared');

    const accepted = service.update('intent-1', (current) => ({ ...current, status: 'sending' }));
    await flush();
    db.completeAll();
    expect((await accepted)?.status).toBe('sending');
    expect(db.records.get('intent-1')?.status).toBe('sending');
  });

  it('lists only intents in the requested scope', async () => {
    const db = stubIndexedDb();
    db.records.set('a', makeIntent('a'));
    db.records.set('b', { ...makeIntent('b'), shopId: 'shop-2' });
    db.records.set('c', { ...makeIntent('c'), userId: 'user-2' });

    const listed = await service.listByScope('user-1', 'shop-1');

    expect(listed.map((intent) => intent.intentId)).toEqual(['a']);
  });
});

function makeIntent(intentId: string): CheckoutIntent {
  return {
    schemaVersion: 1,
    intentId,
    userId: 'user-1',
    shopId: 'shop-1',
    idempotencyKey: `key-${intentId}`,
    request: {
      idempotencyKey: `key-${intentId}`,
      customerId: null,
      customerName: null,
      customerPhone: null,
      paymentMethod: 1,
      paidAmount: 100,
      dueAmount: 0,
      items: [],
      saleDiscount: null,
    },
    createdAt: '2026-11-01T00:00:00.000Z',
    updatedAt: '2026-11-01T00:00:00.000Z',
    status: 'prepared',
    sendingUntil: null,
    saleId: null,
    invoiceNumber: null,
    rejectionCode: null,
  };
}

function flush(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

interface FakeTransaction {
  error: unknown;
  oncomplete: null | (() => void);
  onerror: null | (() => void);
  onabort: null | (() => void);
  objectStore: () => unknown;
}

/** Minimal IndexedDB: requests succeed on a microtask; readwrite transactions commit when the test says so. */
function stubIndexedDb() {
  const records = new Map<string, CheckoutIntent>();
  const openTransactions: FakeTransaction[] = [];

  const request = <T>(produce: () => T) => {
    const req = { result: undefined as T | undefined, error: null as unknown, onsuccess: null as null | (() => void), onerror: null as null | (() => void) };
    queueMicrotask(() => {
      req.result = produce();
      req.onsuccess?.();
    });
    return req;
  };

  const database = {
    objectStoreNames: { contains: () => true },
    createObjectStore: vi.fn(),
    close: vi.fn(),
    transaction: (_name: string, mode: IDBTransactionMode) => {
      const transaction: FakeTransaction = {
        error: null,
        oncomplete: null,
        onerror: null,
        onabort: null,
        objectStore: () => ({
          get: (key: string) => request(() => records.get(key)),
          getAll: () => request(() => [...records.values()]),
          add: (value: CheckoutIntent) => request(() => records.set(value.intentId, value)),
          put: (value: CheckoutIntent) => request(() => records.set(value.intentId, value)),
        }),
      };
      if (mode === 'readwrite') openTransactions.push(transaction);
      return transaction;
    },
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
      queueMicrotask(() => openRequest.onsuccess?.());
      return openRequest;
    }),
  });

  return {
    records,
    completeAll: () => openTransactions.splice(0).forEach((transaction) => transaction.oncomplete?.()),
    failAll: (error: unknown) =>
      openTransactions.splice(0).forEach((transaction) => {
        transaction.error = error;
        transaction.onabort?.();
      }),
  };
}
