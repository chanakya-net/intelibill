import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { convertToParamMap } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { stubMemoryStorage } from '../billing.test-utils';
import type { CheckoutScope } from '../checkout/checkout.models';
import type { CreateRefundRequest, RefundRequestItem, RefundRequestPage } from './refund-request.models';
import {
  configureRefundPage, confirmation, deferred, installRefundSpecGlobals, mountRefundPage, order, otherOrder,
  refundPage, refundRow, saveDraft, savedKey, storageKey, verifiedPayment as refundTarget,
} from './refund-request.test-utils';

beforeEach(() => {
  stubMemoryStorage();
  installRefundSpecGlobals();
});
afterEach(() => {
  vi.unstubAllGlobals();
  TestBed.resetTestingModule();
});

describe('merchant refund request list snapshots', () => {
  it('keeps a freshly acknowledged request when the earlier empty first page arrives after it', async () => {
    const firstPage = deferred<RefundRequestPage>();
    const { create } = configureRefundPage({
      create: async (_scope, command) => refundRow(command),
      list: () => firstPage.promise,
    });
    const tab = mountRefundPage();
    await tab.settle();

    await tab.submit('45.00', 'Acknowledged before the list answered');
    expect(create).toHaveBeenCalledTimes(1);
    expect(tab.rows()).toHaveLength(1);

    firstPage.resolve(refundPage());
    await tab.settle();

    expect(tab.rows()).toHaveLength(1);
    expect(tab.rows()[0].getAttribute('data-refund-id')).toBe(refundRow({ amountPaise: 1, reason: 'x' }).refundRequestId);
    expect(tab.find('refund-request-empty')).toBeNull();
    expect(tab.find('refund-request-submitted')).not.toBeNull();
    expect(tab.find('refund-request-more')).toBeNull();
  });

  it('shows a recovered request once and honors the cursor of the first page that also lists it', async () => {
    saveDraft('Recovered before the list answered');
    const firstPage = deferred<RefundRequestPage>();
    const cursor = '55555555-5555-4555-8555-000000000002';
    const older = refundRow({ amountPaise: 1000, reason: 'Older request' }, 2);
    const { list } = configureRefundPage({
      create: async (_scope, command) => refundRow(command),
      list: (_scope, after) => after ? Promise.resolve(refundPage()) : firstPage.promise,
    });
    const reloaded = mountRefundPage();
    await reloaded.settle();
    expect(reloaded.rows()).toHaveLength(1);

    const recovered = refundRow({ amountPaise: 4500, reason: 'Recovered before the list answered' });
    firstPage.resolve(refundPage([recovered, older], cursor));
    await reloaded.settle();

    const ids = Array.from(reloaded.rows()).map(row => row.getAttribute('data-refund-id'));
    expect(ids).toEqual([recovered.refundRequestId, older.refundRequestId]);
    expect(reloaded.find('refund-request-empty')).toBeNull();

    reloaded.find<HTMLButtonElement>('refund-request-more')!.click();
    await reloaded.settle();
    expect(list.mock.calls.at(-1)?.[1]).toBe(cursor);
    expect(reloaded.rows()).toHaveLength(2);
  });
});

describe('merchant refund request key shared by two deliveries', () => {
  it.each([
    { label: 'a 403 denial', refusal: new HttpErrorResponse({ status: 403, error: { code: 'Billing.DelegationDenied' } }) },
    { label: 'a generic 400', refusal: new HttpErrorResponse({ status: 400 }) },
  ])('keeps the key when $label answers only the first of two deliveries', async ({ refusal }) => {
    const persisted = new Map<string, RefundRequestItem>();
    const firstAnswer = deferred<RefundRequestItem>();
    const sentKeys: string[] = [];
    configureRefundPage({
      create: async (_scope: CheckoutScope, command: CreateRefundRequest) => {
        sentKeys.push(command.operationKey);
        if (sentKeys.length === 1) return firstAnswer.promise;
        if (!persisted.has(command.operationKey)) persisted.set(command.operationKey, refundRow(command));
        if (sentKeys.length === 2) throw new HttpErrorResponse({ status: 0 });
        return persisted.get(command.operationKey)!;
      },
    });

    const original = mountRefundPage();
    await original.settle();
    await original.submit('45.00', 'Sent from two tabs');
    const key = JSON.parse(localStorage.getItem(storageKey)!).command.operationKey as string;

    // The second tab finds the saved command and resends it; licensing commits but the answer is lost.
    const second = mountRefundPage();
    await second.settle();
    expect(sentKeys).toEqual([key, key]);
    expect(persisted.size).toBe(1);

    firstAnswer.reject(refusal);
    await original.settle();
    expect(localStorage.getItem(storageKey)).toContain(key);
    expect(original.find('refund-request-pending')).not.toBeNull();
    original.fixture.destroy();
    second.fixture.destroy();

    const reloaded = mountRefundPage();
    await reloaded.settle();
    expect(sentKeys).toEqual([key, key, key]);
    expect(persisted.size).toBe(1);
    expect(localStorage.getItem(storageKey)).toBeNull();
    expect(reloaded.rows()).toHaveLength(1);
  });

  it('releases the key and keeps the form editable when the only delivery is refused before replay', async () => {
    const { create } = configureRefundPage({
      create: async () => { throw new HttpErrorResponse({ status: 403, error: { code: 'Billing.DelegationDenied' } }); },
    });
    const tab = mountRefundPage();
    await tab.settle();
    await tab.submit('45.00', 'Only delivery');

    expect(create).toHaveBeenCalledTimes(1);
    expect(localStorage.getItem(storageKey)).toBeNull();
    expect(tab.find('refund-request-pending')).toBeNull();
    expect(tab.find<HTMLTextAreaElement>('refund-request-reason')!.value).toBe('Only delivery');
    expect(tab.find<HTMLButtonElement>('refund-request-submit')!.disabled).toBe(false);
  });

  it('does not resend a command another tab settled while this tab still showed it', async () => {
    saveDraft('Settled elsewhere');
    const settled = refundRow({ amountPaise: 4500, reason: 'Settled elsewhere' });
    let committed = false;
    const { create } = configureRefundPage({
      create: async () => { throw new HttpErrorResponse({ status: 503 }); },
      list: async () => refundPage(committed ? [settled] : []),
    });
    const tab = mountRefundPage();
    await tab.settle();
    expect(create).toHaveBeenCalledTimes(1);

    committed = true;
    localStorage.removeItem(storageKey);
    tab.find<HTMLButtonElement>('refund-request-retry')!.click();
    await tab.settle();

    expect(create).toHaveBeenCalledTimes(1);
    expect(tab.find('refund-request-pending')).toBeNull();
    expect(tab.rows()).toHaveLength(1);
  });
});

describe('merchant refund request saved command authority', () => {
  const denied = () => new HttpErrorResponse({ status: 403, error: { code: 'Billing.DelegationDenied' } });

  it.each([
    {
      label: 'account and confirmation reads are denied',
      route: { order, confirmation },
      reads: () => Promise.reject(denied()),
      error: 'permission',
    },
    {
      label: 'the same confirmation is opened under another order with no receipt',
      route: { order: otherOrder, confirmation },
      reads: () => Promise.resolve([]),
      error: 'not found',
    },
  ])('hides and holds the saved command while $label, then recovers it on the authorized route', async scenario => {
    const payload = saveDraft('Private saved reason', 4567);
    let authorized = false;
    const { create, listConfirmations, queryParamMap } = configureRefundPage({
      create: async (_scope, command) => refundRow(command),
      list: () => authorized ? Promise.resolve(refundPage()) : Promise.reject(denied()),
      listConfirmations: (_scope, requestedOrder) => authorized && requestedOrder === order
        ? Promise.resolve([refundTarget])
        : scenario.reads(),
    });
    queryParamMap.next(convertToParamMap(scenario.route));

    const unresolved = mountRefundPage();
    await unresolved.settle();

    expect(listConfirmations).toHaveBeenCalled();
    expect(unresolved.find('refund-request-error')?.textContent).toContain(scenario.error);
    expect(unresolved.find('refund-request-pending')).toBeNull();
    expect(unresolved.find('refund-request-retry')).toBeNull();
    expect(unresolved.find('refund-request-form')).toBeNull();
    expect(unresolved.page.textContent).not.toContain('Private saved reason');
    expect(unresolved.page.textContent).not.toContain('45.67');
    expect(create).not.toHaveBeenCalled();
    expect(localStorage.getItem(storageKey)).toBe(payload);
    unresolved.fixture.destroy();

    authorized = true;
    queryParamMap.next(convertToParamMap({ order, confirmation }));
    const recovered = mountRefundPage();
    await recovered.settle();

    expect(create).toHaveBeenCalledTimes(1);
    expect(create.mock.calls[0][1]).toEqual({
      operationKey: savedKey, orderId: order, confirmationId: confirmation, amountPaise: 4567, reason: 'Private saved reason',
    });
    expect(localStorage.getItem(storageKey)).toBeNull();
    expect(recovered.rows()).toHaveLength(1);
  });

  it('treats a saved command naming another order as unreadable and never sends it', async () => {
    const payload = JSON.stringify({
      schemaVersion: 1,
      command: { operationKey: savedKey, orderId: otherOrder, confirmationId: confirmation, amountPaise: 4567, reason: 'Other order reason' },
      deliveries: 1,
    });
    localStorage.setItem(storageKey, payload);
    const { create } = configureRefundPage({ create: async (_scope, command) => refundRow(command) });
    const tab = mountRefundPage();
    await tab.settle();

    expect(create).not.toHaveBeenCalled();
    expect(localStorage.getItem(storageKey)).toBe(payload);
    expect(tab.find('refund-request-unreadable')).not.toBeNull();
    expect(tab.find('refund-request-pending')).toBeNull();
    expect(tab.page.textContent).not.toContain('Other order reason');
    expect(tab.find<HTMLTextAreaElement>('refund-request-reason')!.value).toBe('');
  });
});
