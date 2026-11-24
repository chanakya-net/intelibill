import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { convertToParamMap } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { stubMemoryStorage } from '../billing.test-utils';
import { REFUND_PAGE_SIZE, type RefundRequestItem, type RefundRequestPage } from './refund-request.models';
import {
  configureRefundPage, deferred, installRefundSpecGlobals, mountRefundPage, refundPage, refundRow,
  saveDraft, storageKey,
} from './refund-request.test-utils';

const otherAccount = '22222222-2222-4222-8222-222222222222';

function makePageRows(count: number, startIndex = 1): RefundRequestItem[] {
  return Array.from({ length: count }, (_, offset) =>
    refundRow({ amountPaise: 1000 + offset, reason: `Existing request ${startIndex + offset}` }, startIndex + offset));
}

function rowIds(rows: NodeListOf<Element>): string[] {
  return Array.from(rows).map(row => row.getAttribute('data-refund-id') ?? '');
}

async function mountPendingWithDeferredPages(reason: string, pageTwoRows: readonly RefundRequestItem[] = []) {
  saveDraft(reason);
  const first25 = makePageRows(REFUND_PAGE_SIZE);
  const cursor = first25.at(-1)!.refundRequestId;
  const originalPage = deferred<RefundRequestPage>();
  const refreshPage = deferred<RefundRequestPage>();
  let firstPageCalls = 0;

  const backend = configureRefundPage({
    create: async () => { throw new HttpErrorResponse({ status: 503 }); },
    list: (_scope, after) => {
      if (after === cursor) return Promise.resolve(refundPage(pageTwoRows, null));
      return ++firstPageCalls === 1 ? originalPage.promise : refreshPage.promise;
    },
  });

  const tab = mountRefundPage();
  await tab.settle();
  return { ...backend, tab, first25, cursor, originalPage, refreshPage };
}

async function triggerCrossTabRefresh(tab: ReturnType<typeof mountRefundPage>) {
  localStorage.removeItem(storageKey);
  tab.find<HTMLButtonElement>('refund-request-retry')!.click();
  await tab.settle();
}

beforeEach(() => {
  stubMemoryStorage();
  installRefundSpecGlobals();
});
afterEach(() => {
  vi.unstubAllGlobals();
  TestBed.resetTestingModule();
});

describe('merchant refund request list-read sequencing', () => {
  it('keeps the newer refresh cursor when an older 25-row initial page arrives last with a null cursor', async () => {
    const settled = refundRow({ amountPaise: 4500, reason: 'Settled in another tab' }, 26);
    const { tab, create, list, first25, cursor, originalPage, refreshPage } =
      await mountPendingWithDeferredPages('Settled in another tab', [settled]);
    expect(create).toHaveBeenCalledTimes(1);

    await triggerCrossTabRefresh(tab);
    expect(create).toHaveBeenCalledTimes(1);

    refreshPage.resolve(refundPage(first25, cursor));
    await tab.settle();
    expect(tab.rows()).toHaveLength(REFUND_PAGE_SIZE);
    expect(tab.find('refund-request-more')).not.toBeNull();

    originalPage.resolve(refundPage(first25, null));
    await tab.settle();
    expect(tab.find('refund-request-more')).not.toBeNull();
    expect(tab.find('refund-request-error')).toBeNull();

    tab.find<HTMLButtonElement>('refund-request-more')!.click();
    await tab.settle();

    const ids = rowIds(tab.rows());
    expect(ids).toHaveLength(REFUND_PAGE_SIZE + 1);
    expect(ids.filter(id => id === settled.refundRequestId)).toHaveLength(1);
    expect(create).toHaveBeenCalledTimes(1);
    expect(list.mock.calls.at(-1)?.[1]).toBe(cursor);
    expect(tab.find('refund-request-more')).toBeNull();
  });

  it('ignores an older 25-row initial page that resolves while a newer settlement refresh is still in flight', async () => {
    const settled = refundRow({ amountPaise: 4500, reason: 'Settled while loading' }, 26);
    const { tab, create, first25, cursor, originalPage, refreshPage } =
      await mountPendingWithDeferredPages('Settled while loading', [settled]);

    await triggerCrossTabRefresh(tab);

    originalPage.resolve(refundPage(first25, null));
    await tab.settle();
    expect(tab.rows()).toHaveLength(0);
    expect(tab.find('refund-request-empty')).toBeNull();
    expect(tab.page.textContent).toContain('Loading');

    refreshPage.resolve(refundPage(first25, cursor));
    await tab.settle();
    expect(tab.rows()).toHaveLength(REFUND_PAGE_SIZE);
    expect(tab.find<HTMLButtonElement>('refund-request-more')!.disabled).toBe(false);

    tab.find<HTMLButtonElement>('refund-request-more')!.click();
    await tab.settle();
    expect(rowIds(tab.rows()).filter(id => id === settled.refundRequestId)).toHaveLength(1);
    expect(create).toHaveBeenCalledTimes(1);
  });

  it.each([
    { label: 'while the newer refresh is still in flight', rejectBeforeRefresh: true },
    { label: 'after the newer refresh already succeeded', rejectBeforeRefresh: false },
  ])('ignores a stale initial failure arriving $label', async ({ rejectBeforeRefresh }) => {
    const settled = refundRow({ amountPaise: 4500, reason: 'Settled before stale error' }, 1);
    const { tab, originalPage, refreshPage } = await mountPendingWithDeferredPages('Settled before stale error');
    await triggerCrossTabRefresh(tab);

    if (rejectBeforeRefresh) {
      originalPage.reject(new HttpErrorResponse({ status: 503 }));
      await tab.settle();
      expect(tab.find('refund-request-error')).toBeNull();
      expect(tab.find('refund-request-empty')).toBeNull();
      expect(tab.page.textContent).toContain('Loading');
      refreshPage.resolve(refundPage([settled], null));
      await tab.settle();
    } else {
      refreshPage.resolve(refundPage([settled], null));
      await tab.settle();
      originalPage.reject(new HttpErrorResponse({ status: 503 }));
      await tab.settle();
    }

    expect(tab.rows()).toHaveLength(1);
    expect(tab.find('refund-request-error')).toBeNull();
    expect(tab.find('refund-request-empty')).toBeNull();
  });

  it.each([
    { label: 'renders rows', items: [refundRow({ amountPaise: 4500, reason: 'Settled row' }, 1)], expectEmpty: false },
    { label: 'renders empty state', items: [], expectEmpty: true },
  ])('clears an initial list failure when a later refresh succeeds and $label', async ({ items, expectEmpty }) => {
    saveDraft('Refresh after failure');
    let calls = 0;
    const { create } = configureRefundPage({
      create: async () => { throw new HttpErrorResponse({ status: 503 }); },
      list: () => (++calls === 1
        ? Promise.reject(new HttpErrorResponse({ status: 503 }))
        : Promise.resolve(refundPage(items, null))),
    });

    const tab = mountRefundPage();
    await tab.settle();
    expect(tab.find('refund-request-error')).not.toBeNull();
    expect(tab.find('refund-request-empty')).toBeNull();

    await triggerCrossTabRefresh(tab);

    expect(create).toHaveBeenCalledTimes(1);
    expect(tab.find('refund-request-error')).toBeNull();
    expect(tab.rows()).toHaveLength(items.length);
    if (expectEmpty) expect(tab.find('refund-request-empty')).not.toBeNull();
    else expect(tab.find('refund-request-empty')).toBeNull();
  });

  it.each([
    { label: 'refresh completes before stale load-more', refreshFirst: true },
    { label: 'stale load-more completes before refresh', refreshFirst: false },
  ])('supersedes in-flight pagination on settlement refresh ($label)', async ({ refreshFirst }) => {
    saveDraft('Settled during pagination');
    const first25 = makePageRows(REFUND_PAGE_SIZE);
    const oldCursor = first25.at(-1)!.refundRequestId;
    const freshCursor = '55555555-5555-4555-8555-000000000090';
    const staleRow = refundRow({ amountPaise: 1500, reason: 'Stale page two row' }, 27);
    const settled = refundRow({ amountPaise: 4500, reason: 'Settled during pagination' }, 28);
    const staleMore = deferred<RefundRequestPage>();
    const refreshPage = deferred<RefundRequestPage>();
    let firstPageCalls = 0;

    const { create, list } = configureRefundPage({
      create: async () => { throw new HttpErrorResponse({ status: 503 }); },
      list: (_scope, after) => {
        if (after === oldCursor) return staleMore.promise;
        if (after === freshCursor) return Promise.resolve(refundPage([settled], null));
        return ++firstPageCalls === 1 ? Promise.resolve(refundPage(first25, oldCursor)) : refreshPage.promise;
      },
    });

    const tab = mountRefundPage();
    await tab.settle();
    tab.find<HTMLButtonElement>('refund-request-more')!.click();
    await tab.settle();
    await triggerCrossTabRefresh(tab);

    if (refreshFirst) {
      refreshPage.resolve(refundPage(first25, freshCursor));
      await tab.settle();
      staleMore.resolve(refundPage([staleRow], null));
      await tab.settle();
    } else {
      staleMore.resolve(refundPage([staleRow], null));
      await tab.settle();
      expect(tab.find<HTMLButtonElement>('refund-request-more')!.disabled).toBe(true);
      refreshPage.resolve(refundPage(first25, freshCursor));
      await tab.settle();
    }

    expect(rowIds(tab.rows())).not.toContain(staleRow.refundRequestId);
    tab.find<HTMLButtonElement>('refund-request-more')!.click();
    await tab.settle();
    expect(rowIds(tab.rows()).filter(id => id === settled.refundRequestId)).toHaveLength(1);
    expect(list.mock.calls.at(-1)?.[1]).toBe(freshCursor);
    expect(create).toHaveBeenCalledTimes(1);
  });

  it('preserves a locally acknowledged row across refresh and deduplicates it when Load more reaches it', async () => {
    const first25 = makePageRows(REFUND_PAGE_SIZE);
    const cursor = first25.at(-1)!.refundRequestId;
    const acknowledged = refundRow({ amountPaise: 4500, reason: 'Locally acknowledged first' }, 26);
    const settledElsewhere = refundRow({ amountPaise: 3000, reason: 'Settled in another tab' }, 27);
    let createCalls = 0;
    let firstPageCalls = 0;

    const { create } = configureRefundPage({
      create: async (_scope, command) => {
        if (++createCalls === 1) return { ...acknowledged, reason: command.reason, amountPaise: command.amountPaise };
        throw new HttpErrorResponse({ status: 503 });
      },
      list: (_scope, after) => {
        if (after === cursor) return Promise.resolve(refundPage([acknowledged, settledElsewhere], null));
        return ++firstPageCalls === 1
          ? Promise.resolve(refundPage(first25, null))
          : Promise.resolve(refundPage(first25, cursor));
      },
    });

    const tab = mountRefundPage();
    await tab.settle();
    await tab.submit('45.00', 'Locally acknowledged first');
    await tab.submit('30.00', 'Settled in another tab');
    await triggerCrossTabRefresh(tab);

    expect(rowIds(tab.rows()).filter(id => id === acknowledged.refundRequestId)).toHaveLength(1);
    tab.find<HTMLButtonElement>('refund-request-more')!.click();
    await tab.settle();

    const ids = rowIds(tab.rows());
    expect(ids).toHaveLength(REFUND_PAGE_SIZE + 2);
    expect(ids.filter(id => id === acknowledged.refundRequestId)).toHaveLength(1);
    expect(ids.filter(id => id === settledElsewhere.refundRequestId)).toHaveLength(1);
    expect(create).toHaveBeenCalledTimes(2);
  });

  it('drops an in-flight settlement refresh and prior acknowledged rows when the active scope changes', async () => {
    const oldRefresh = deferred<RefundRequestPage>();
    const oldAck = refundRow({ amountPaise: 4500, reason: 'Old scope acknowledged' }, 1);
    let createCalls = 0;
    let listCalls = 0;

    const { paramMap, scope } = configureRefundPage({
      create: async () => (++createCalls === 1 ? oldAck : Promise.reject(new HttpErrorResponse({ status: 503 }))),
      list: (currentScope) => {
        if (++listCalls === 1) return Promise.resolve(refundPage());
        if (listCalls === 2) return oldRefresh.promise;
        return Promise.resolve({ schemaVersion: 1, accountId: currentScope.account, items: [], nextCursor: null });
      },
    });

    const tab = mountRefundPage();
    await tab.settle();
    await tab.submit('45.00', 'Old scope acknowledged');
    await tab.submit('30.00', 'Old scope settled');
    await triggerCrossTabRefresh(tab);

    scope.set({ userId: 'other-user', shopId: 'shop-2' });
    paramMap.next(convertToParamMap({ accountId: otherAccount }));
    await tab.settle();

    oldRefresh.resolve(refundPage([refundRow({ amountPaise: 3000, reason: 'Old scope settled' }, 2)], null));
    await tab.settle();

    expect(tab.rows()).toHaveLength(0);
    expect(tab.find('refund-request-empty')).not.toBeNull();
    expect(tab.find('refund-request-error')).toBeNull();
  });
});
