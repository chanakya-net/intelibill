import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom, of, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import {
  FinancialOperationIntentService,
  ForeignFinancialOperationScopeError,
  classifyFinancialOperationFailure,
} from './financial-operation-intent.service';

interface PaymentPayload {
  readonly amount: number;
  readonly paymentDate: string;
  readonly notes: string | null;
}

const shopA = { userId: 'user-1', shopId: 'shop-a' };
const shopB = { userId: 'user-1', shopId: 'shop-b' };
const payload: PaymentPayload = { amount: 500, paymentDate: '2026-11-01', notes: 'NEFT' };

class MemoryStorage {
  private readonly values = new Map<string, string>();
  failWrites = false;

  getItem(key: string): string | null {
    return this.values.get(key) ?? null;
  }

  setItem(key: string, value: string): void {
    if (this.failWrites) throw new DOMException('Quota exceeded', 'QuotaExceededError');
    this.values.set(key, value);
  }

  removeItem(key: string): void {
    this.values.delete(key);
  }

  clear(): void {
    this.values.clear();
  }
}

function freshService(): FinancialOperationIntentService {
  return TestBed.runInInjectionContext(() => new FinancialOperationIntentService());
}

describe('FinancialOperationIntentService', () => {
  let service: FinancialOperationIntentService;
  let storage: MemoryStorage;
  let originalLocalStorage: PropertyDescriptor | undefined;

  beforeEach(() => {
    originalLocalStorage = Object.getOwnPropertyDescriptor(globalThis, 'localStorage');
    storage = new MemoryStorage();
    Object.defineProperty(globalThis, 'localStorage', { configurable: true, value: storage });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(FinancialOperationIntentService);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    if (originalLocalStorage) {
      Object.defineProperty(globalThis, 'localStorage', originalLocalStorage);
    } else {
      delete (globalThis as { localStorage?: Storage }).localStorage;
    }
  });

  it('persists the frozen payload and key before anything is sent', () => {
    const sender = vi.fn();
    const intent = service.prepare(shopA, 'supplier-payment', 'supplier-1', payload);

    expect(sender).not.toHaveBeenCalled();
    expect(Object.isFrozen(intent.payload)).toBe(true);
    expect(freshService().pending(shopA, 'supplier-payment', 'supplier-1')?.operationKey).toBe(intent.operationKey);
  });

  it('storageFailureSendsNothing', () => {
    storage.failWrites = true;

    expect(() => service.prepare(shopA, 'supplier-payment', 'supplier-1', payload)).toThrow();
    expect(service.pending(shopA, 'supplier-payment', 'supplier-1')).toBeNull();
  });

  it('reloadAfterResponseLossReusesFrozenPayload', async () => {
    const intent = service.prepare(shopA, 'customer-payment', 'customer-1', payload);
    await expect(
      firstValueFrom(service.send(shopA, intent, () => throwError(() => new HttpErrorResponse({ status: 0 })))),
    ).rejects.toBeInstanceOf(HttpErrorResponse);

    const reloaded = freshService();
    const pending = reloaded.pending<PaymentPayload>(shopA, 'customer-payment', 'customer-1');
    expect(pending?.status).toBe('unknown');

    const sender = vi.fn((_: PaymentPayload, __: string) => of({ entryId: 'entry-1' }));
    await firstValueFrom(reloaded.send(shopA, pending!, sender));

    expect(sender).toHaveBeenCalledWith(payload, intent.operationKey);
    expect(reloaded.pending(shopA, 'customer-payment', 'customer-1')).toBeNull();
  });

  it('shopSwitchCannotSendForeignIntent', async () => {
    const intent = service.prepare(shopA, 'supplier-payment', 'supplier-1', payload);
    const sender = vi.fn(() => of({}));

    await expect(firstValueFrom(service.send(shopB, intent, sender))).rejects.toBeInstanceOf(
      ForeignFinancialOperationScopeError,
    );
    await expect(firstValueFrom(service.check(shopB, intent))).rejects.toBeInstanceOf(
      ForeignFinancialOperationScopeError,
    );

    expect(sender).not.toHaveBeenCalled();
    expect(service.pending(shopB, 'supplier-payment', 'supplier-1')).toBeNull();
    expect(service.pending(shopA, 'supplier-payment', 'supplier-1')?.operationKey).toBe(intent.operationKey);
  });

  it('keeps an unknown outcome on timeout and server errors', async () => {
    for (const status of [408, 429, 503]) {
      const intent = service.prepare(shopA, 'supplier-payment', `supplier-${status}`, payload);
      await expect(
        firstValueFrom(service.send(shopA, intent, () => throwError(() => new HttpErrorResponse({ status })))),
      ).rejects.toBeTruthy();
      expect(service.pending(shopA, 'supplier-payment', `supplier-${status}`)?.status).toBe('unknown');
    }
  });

  it('discards a definitively refused intent so a correction is a new action', async () => {
    const intent = service.prepare(shopA, 'supplier-payment', 'supplier-1', payload);

    await expect(
      firstValueFrom(service.send(shopA, intent, () => throwError(() => new HttpErrorResponse({ status: 400 })))),
    ).rejects.toBeTruthy();

    expect(service.pending(shopA, 'supplier-payment', 'supplier-1')).toBeNull();
    const corrected = service.prepare(shopA, 'supplier-payment', 'supplier-1', { ...payload, amount: 450 });
    expect(corrected.operationKey).not.toBe(intent.operationKey);
  });

  it('check clears only after the matching committed receipt is found', async () => {
    const http = TestBed.inject(HttpTestingController);
    const intent = service.prepare(shopA, 'customer-payment', 'customer-1', payload);

    const missing = firstValueFrom(service.check(shopA, intent));
    http
      .expectOne((request) => request.url.endsWith(`/financial-operations/customer-payment/${intent.operationKey}`))
      .flush({ title: 'Operation.NotFound' }, { status: 404, statusText: 'Not Found' });
    await expect(missing).resolves.toBeNull();
    expect(service.pending(shopA, 'customer-payment', 'customer-1')).not.toBeNull();

    const found = firstValueFrom(service.check<{ entryId: string }>(shopA, intent));
    http
      .expectOne((request) => request.url.endsWith(`/financial-operations/customer-payment/${intent.operationKey}`))
      .flush({ operationKey: intent.operationKey, response: { entryId: 'entry-1' } });
    await expect(found).resolves.toEqual({ entryId: 'entry-1' });
    expect(service.pending(shopA, 'customer-payment', 'customer-1')).toBeNull();
    http.verify();
  });
});

describe('classifyFinancialOperationFailure', () => {
  it.each([0, 408, 429, 500, 503])('treats %s as uncertain', (status) => {
    expect(classifyFinancialOperationFailure(new HttpErrorResponse({ status }))).toBe('uncertain');
  });

  it.each([400, 403, 404, 409])('treats %s as rejected', (status) => {
    expect(classifyFinancialOperationFailure(new HttpErrorResponse({ status }))).toBe('rejected');
  });

  it('treats a non-HTTP failure as uncertain', () => {
    expect(classifyFinancialOperationFailure(new Error('socket hang up'))).toBe('uncertain');
  });
});
