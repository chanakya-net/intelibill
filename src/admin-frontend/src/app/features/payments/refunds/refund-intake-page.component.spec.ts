import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AdminSessionService } from '../../auth/admin-session.service';
import {
  REFUND_INTAKE_STATUSES, RefundIntakeApi, type RefundIntakeItem, type RefundIntakePage,
} from './refund-intake-api.service';
import { RefundIntakePageComponent } from './refund-intake-page.component';

const account = '11111111-aaaa-4111-8111-1111111111bb';
const mixedCaseAccount = '11111111-AAAA-4111-8111-1111111111BB';
const otherAccount = '22222222-2222-4222-8222-222222222222';
const order = '33333333-3333-4333-8333-333333333333';
const confirmation = '44444444-4444-4444-8444-444444444444';
const actor = '55555555-5555-4555-8555-555555555555';
const at = '2028-01-31T04:30:00Z';

function makeItem(status: RefundIntakeItem['status'], index: number, accountId = account): RefundIntakeItem {
  return {
    refundRequestId: `66666666-6666-4666-8666-${String(index + 1).padStart(12, '0')}`,
    accountId,
    orderId: order,
    confirmationId: confirmation,
    actorId: actor,
    paymentSource: 'Online',
    status,
    amountPaise: 4500,
    paymentAmountPaise: 12000,
    currency: 'INR',
    reason: `Intake reason ${index + 1}`,
    requestedAt: at,
    providerId: 'development',
    providerAccountId: 'dev-account',
    environment: 'Development',
    configurationVersion: 'sim/1',
  };
}

function setup(options: {
  billing?: boolean;
  initialAccount?: string | null;
  items?: readonly RefundIntakeItem[];
  list?: (account: string, session: string, after?: string) => Promise<RefundIntakePage>;
} = {}) {
  const initialQuery = options.initialAccount === null ? {} : { account: options.initialAccount ?? account };
  const queryParamMap = new BehaviorSubject(convertToParamMap(initialQuery));
  const adminId = signal('finance-admin');
  const sessionId = signal('session-1');
  const capabilities = signal(options.billing === false ? ['support'] : ['billing']);
  const list = vi.fn(options.list ?? (async (acc: string): Promise<RefundIntakePage> => ({
    schemaVersion: 1,
    accountId: acc,
    items: options.items ?? [],
    nextCursor: null,
  })));

  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: ActivatedRoute, useValue: { queryParamMap } },
      { provide: AdminSessionService, useValue: { adminId, sessionId, capabilities } },
      { provide: RefundIntakeApi, useValue: { list } },
    ],
  });

  const router = TestBed.inject(Router);
  const navigate = vi.spyOn(router, 'navigate').mockImplementation(async (_commands, extras) => {
    const next = (extras?.queryParams as Record<string, string> | undefined)?.['account'];
    if (next) queryParamMap.next(convertToParamMap({ account: next }));
    return true;
  });

  const fixture = TestBed.createComponent(RefundIntakePageComponent);
  fixture.detectChanges();
  const settle = async () => {
    await new Promise(resolve => setTimeout(resolve, 0));
    fixture.detectChanges();
  };
  return { fixture, page: fixture.nativeElement as HTMLElement, queryParamMap, adminId, sessionId, list, navigate, settle };
}

afterEach(() => TestBed.resetTestingModule());

describe('admin refund intake page', () => {
  it('requires billing capability before loading any account intake', async () => {
    const { page, list, settle } = setup({ billing: false });
    await settle();

    expect(page.querySelector('[data-testid="refund-intake-denied"]')?.textContent)
      .toContain('Billing access is required');
    expect(list).not.toHaveBeenCalled();
  });

  it('renders all five status distinctions, actor id and frozen provider pin', async () => {
    const items = REFUND_INTAKE_STATUSES.map((status, index) => makeItem(status, index));
    const { page, list, settle } = setup({ items });
    await settle();

    expect(list).toHaveBeenCalledExactlyOnceWith(account, 'session-1', undefined);
    const labels = Array.from(page.querySelectorAll('[data-testid="refund-intake-status"]'))
      .map(el => el.textContent?.trim() ?? '');
    const details = Array.from(page.querySelectorAll('[data-testid="refund-intake-status-detail"]'))
      .map(el => el.textContent?.trim() ?? '');

    expect(labels).toHaveLength(5);
    expect(new Set(labels).size).toBe(5);
    expect(new Set(details).size).toBe(5);
    expect(page.querySelector('[data-testid="refund-intake-actor"]')?.textContent).toContain(actor);
    expect(page.querySelector('[data-testid="refund-intake-provider"]')?.textContent).toContain('development');
    expect(page.querySelector('[data-testid="refund-intake-provider"]')?.textContent).toContain('dev-account');
  });

  it('canonicalizes mixed-case GUIDs for typed search and direct query URLs', async () => {
    const row = makeItem('Requested', 0, account);
    const direct = setup({ initialAccount: mixedCaseAccount, items: [row] });
    await direct.settle();

    expect(direct.list).toHaveBeenCalledExactlyOnceWith(account, 'session-1', undefined);
    expect(direct.page.querySelectorAll('[data-testid="refund-intake-row"]')).toHaveLength(1);
    TestBed.resetTestingModule();

    const searched = setup({ initialAccount: null, items: [row] });
    await searched.settle();
    const input = searched.page.querySelector('[data-testid="refund-intake-account"]') as HTMLInputElement;
    input.value = `  ${mixedCaseAccount}  `;
    (searched.page.querySelector('[data-testid="refund-intake-search"]') as HTMLButtonElement).click();
    await searched.settle();

    expect(searched.navigate).toHaveBeenCalledWith([], expect.objectContaining({ queryParams: { account } }));
    expect(searched.list).toHaveBeenCalledExactlyOnceWith(account, 'session-1', undefined);
    expect(searched.page.querySelectorAll('[data-testid="refund-intake-row"]')).toHaveLength(1);
  });

  it('discards stale intake responses when the account scope changes', async () => {
    let resolveFirst!: (page: RefundIntakePage) => void;
    let calls = 0;
    const { page, queryParamMap, settle } = setup({
      list: (acc) => {
        calls++;
        if (calls === 1) return new Promise<RefundIntakePage>(resolve => { resolveFirst = resolve; });
        return Promise.resolve({ schemaVersion: 1, accountId: acc, items: [], nextCursor: null });
      },
    });

    queryParamMap.next(convertToParamMap({ account: otherAccount }));
    await settle();

    resolveFirst({ schemaVersion: 1, accountId: account, items: [makeItem('Requested', 0)], nextCursor: null });
    await settle();

    expect(page.querySelectorAll('[data-testid="refund-intake-row"]')).toHaveLength(0);
    expect(page.querySelector('[data-testid="refund-intake-empty"]')).not.toBeNull();
  });
});

describe('RefundIntakeApi canonical GUID validation', () => {
  it('accepts lowercase server responses for mixed-case account IDs and rejects cross-account payloads', async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const api = TestBed.inject(RefundIntakeApi);
    const http = TestBed.inject(HttpTestingController);

    const okPromise = api.list(mixedCaseAccount, 'session-1');
    const req1 = http.expectOne(`/api/admin/accounts/${account}/refund-requests`);
    req1.flush({ schemaVersion: 1, accountId: account, items: [makeItem('Requested', 0, account)], nextCursor: null });
    const okPage = await okPromise;
    expect(okPage.accountId).toBe(account);
    expect(okPage.items).toHaveLength(1);

    const wrongPagePromise = api.list(mixedCaseAccount, 'session-1');
    const req2 = http.expectOne(`/api/admin/accounts/${account}/refund-requests`);
    req2.flush({ schemaVersion: 1, accountId: otherAccount, items: [], nextCursor: null });
    await expect(wrongPagePromise).rejects.toThrow('Invalid refund intake page');

    const wrongRowPromise = api.list(mixedCaseAccount, 'session-1');
    const req3 = http.expectOne(`/api/admin/accounts/${account}/refund-requests`);
    req3.flush({ schemaVersion: 1, accountId: account, items: [makeItem('Requested', 0, otherAccount)], nextCursor: null });
    await expect(wrongRowPromise).rejects.toThrow('Invalid refund intake page');
    http.verify();
  });
});
