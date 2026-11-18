import { afterEach, describe, expect, it, vi } from 'vitest';
import { ManualPaymentDrafts } from '../manual-payment-drafts';
import type { ReviewCommand } from './payment-review-api.service';
import { PaymentReviewDrafts } from './payment-review-drafts';

const scope = { admin: 'finance', account: 'account', order: 'case' };
const original: ReviewCommand = {
  operationKey: 'original', expectedVersion: 0, action: 'RecoverAcceptedTerms',
  reason: 'Original terms verified', evidence: 'FIXTURE-RECEIPT', sourceHash: 'a'.repeat(64),
};

function installStorage() {
  const databases = new Map<string, Map<string, unknown>>();
  vi.stubGlobal('indexedDB', {
    open(name: string) {
      const upgrade = !databases.has(name);
      const records = databases.get(name) ?? new Map<string, unknown>();
      databases.set(name, records);
      const request = {
        onupgradeneeded: () => {}, onsuccess: () => {},
        result: {
          createObjectStore: () => {},
          close: () => {},
          transaction() {
            const tx = {
              oncomplete: () => {},
              objectStore: () => ({
                get(key: string) {
                  const read = { result: structuredClone(records.get(key)), onsuccess: () => {} };
                  queueMicrotask(() => {
                    read.onsuccess();
                    tx.oncomplete();
                  });
                  return read;
                },
                put(value: unknown, key: string) { records.set(key, structuredClone(value)); },
              }),
            };
            return tx;
          },
        },
      };
      queueMicrotask(() => {
        if (upgrade) request.onupgradeneeded();
        request.onsuccess();
      });
      return request;
    },
  });
}

afterEach(() => vi.unstubAllGlobals());

describe('payment review drafts', () => {
  it('keeps the frozen original across instances, separates scopes and preserves the settled revision', async () => {
    installStorage();
    const first = new PaymentReviewDrafts();
    const second = new PaymentReviewDrafts();
    const replacement = { ...original, operationKey: 'replacement', evidence: 'OTHER-RECEIPT' };
    const claimed = { revision: 0, pending: original };
    expect(await first.claim(scope, 0, original)).toEqual(claimed);
    expect(await second.read(scope)).toEqual(claimed);
    expect(await second.claim(scope, 0, replacement)).toEqual(claimed);
    for (const other of [{ ...scope, admin: 'other' }, { ...scope, account: 'other' }, { ...scope, order: 'other' }])
      expect(await second.read(other)).toEqual({ revision: 0 });
    expect(await new ManualPaymentDrafts().read(scope)).toEqual({ revision: 0 });

    await second.settle(scope, replacement.operationKey);
    expect(await first.read(scope)).toEqual(claimed);
    await second.settle(scope, original.operationKey);
    expect(await first.claim(scope, 0, replacement)).toEqual({ revision: 1 });
    expect(await first.claim(scope, 1, replacement)).toEqual({ revision: 1, pending: replacement });
    await second.settle(scope, original.operationKey);
    expect(await first.read(scope)).toEqual({ revision: 1, pending: replacement });
  });

  it('rejects a durable claim when IndexedDB cannot open', async () => {
    const unavailable = new DOMException('Storage unavailable', 'SecurityError');
    vi.stubGlobal('indexedDB', {
      open() {
        const request = { error: unavailable, onerror: () => {} };
        queueMicrotask(() => request.onerror());
        return request;
      },
    });
    await expect(new PaymentReviewDrafts().claim(scope, 0, original)).rejects.toBe(unavailable);
  });
});
