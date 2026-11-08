import { TillVerificationStore } from './till-verification-store';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { IDBFactory } from 'fake-indexeddb';
import { beforeEach, afterEach, describe, it, expect, vi } from 'vitest';
import { AuthService } from '../../../core/auth/auth.service';
import { TillContextService } from './till-context.service';
import { TillSession } from './till.models';
const today = () =>
  new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Kolkata',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date());
const auth = signal<any>({
  user: { id: 'u' },
  activeShopId: 's',
  shops: [{ shopId: 's', role: 'Owner' }],
});
const tick = () => new Promise((r) => setTimeout(r, 20));
const session = () =>
  ({
    id: 'drawer',
    cashierId: 'u',
    businessDate: today(),
    businessTimeZoneId: 'Asia/Kolkata',
    state: 'Open',
  }) as TillSession;
function setup() {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: AuthService, useValue: { session: auth } },
    ],
  });
  const context = TestBed.inject(TillContextService);
  TestBed.tick();
  return { context, http: TestBed.inject(HttpTestingController) };
}
async function verify(context: TillContextService, http: HttpTestingController) {
  await tick();
  (await vi.waitFor(() => http.expectOne((r) => r.url.endsWith('/capabilities')))).flush({
    enabled: true,
    mixedTenderEnabled: false,
    businessDate: today(),
  });
  await tick();
  (await vi.waitFor(() => http.expectOne((r) => r.url.endsWith('/sessions')))).flush({
    items: [session()],
    page: 1,
    pageSize: 20,
    totalCount: 1,
  });
  await tick();
}
describe('TillContextService verified owner selection', () => {
  beforeEach(() => {
    vi.stubGlobal('indexedDB', new IDBFactory());
    auth.set({ user: { id: 'u' }, activeShopId: 's', shops: [{ shopId: 's', role: 'Owner' }] });
  });
  afterEach(() => {
    TestBed.resetTestingModule();
    vi.unstubAllGlobals();
  });
  it('never selects the first open drawer and freezes only explicit own same-day offline evidence', async () => {
    const { context, http } = setup();
    await verify(context, http);
    expect(context.selected()).toBeNull();
    expect(() => context.saleEvidence(1, 10)).toThrow();
    context.select('drawer');
    expect(await context.offlineEvidence(1, 10)).toEqual({
      till: { sessionId: 'drawer', overrideReason: null },
      tillBusinessDate: today(),
      tillCashierId: 'u',
      tenderLines: [{ method: 'Cash', appliedAmount: 10 }],
    });
    http.verify();
  });
  it('restores a previously verified own drawer after offline reload without opening it', async () => {
    let { context, http } = setup();
    await verify(context, http);
    context.select('drawer');
    await context.offlineEvidence(1, 10);
    TestBed.resetTestingModule();
    ({ context, http } = setup());
    await tick();
    (await vi.waitFor(() => http.expectOne((r) => r.url.endsWith('/capabilities')))).error(
      new ProgressEvent('offline'),
    );
    await tick();
    expect((await context.offlineEvidence(1, 10))['till']?.sessionId).toBe('drawer');
    http.expectNone((r) => r.method === 'POST');
    http.verify();
  });
  it('does not manufacture known disabled capability after network failure', async () => {
    const { context, http } = setup();
    await tick();
    (await vi.waitFor(() => http.expectOne((r) => r.url.endsWith('/capabilities')))).error(
      new ProgressEvent('offline'),
    );
    await tick();
    expect(context.capability()).toBeNull();
    await expect(context.offlineEvidence(1, 10)).rejects.toThrow();
    http.verify();
  });
  it('refuses overdue or another cashier for offline trading even with manager override', async () => {
    const { context, http } = setup();
    await verify(context, http);
    context.select('drawer');
    context.selected.update((s) => ({ ...s!, businessDate: '2026-01-01' }));
    await expect(context.offlineEvidence(1, 10)).rejects.toThrow();
    context.selected.set({ ...session(), cashierId: 'other' });
    context.overrideReason.set('Manager override');
    await expect(context.offlineEvidence(1, 10)).rejects.toThrow();
    http.verify();
  });
  it('clears selection/capability on scope switch and ignores the left owner response', async () => {
    const { context, http } = setup();
    await tick();
    const old = await vi.waitFor(() => http.expectOne((r) => r.url.endsWith('/capabilities')));
    auth.set({ user: { id: 'v' }, activeShopId: 'other', shops: [] });
    TestBed.tick();
    old.flush({ enabled: true, mixedTenderEnabled: false, businessDate: today() });
    await tick();
    expect(context.selected()).toBeNull();
    expect(context.capability()).toBeNull();
    (await vi.waitFor(() => http.expectOne((r) => r.url.endsWith('/capabilities')))).error(
      new ProgressEvent('offline'),
    );
    await tick();
    await expect(context.offlineEvidence(1, 10)).rejects.toThrow();
    http.verify();
  });
  it('freezes the selected offline drawer before waiting for its verification write', async () => {
    const { context, http } = setup();
    await verify(context, http);
    let release!: () => void;
    const barrier = new Promise<void>((r) => (release = r));
    const store = TestBed.inject(TillVerificationStore);
    const real = store.save.bind(store);
    vi.spyOn(store, 'save').mockImplementation(async (value) => {
      await barrier;
      await real(value);
    });
    context.select('drawer');
    const frozen = context.offlineEvidence(1, 10);
    context.selected.set({ ...session(), id: 'different' });
    release();
    expect((await frozen).till?.sessionId).toBe('drawer');
    http.verify();
  });
  it('keeps explicit drawer choices reachable beyond the first session page', async () => {
    const { context, http } = setup();
    await tick();
    (await vi.waitFor(() => http.expectOne((r) => r.url.endsWith('/capabilities')))).flush({
      enabled: true,
      mixedTenderEnabled: false,
      businessDate: today(),
    });
    (await vi.waitFor(() => http.expectOne((r) => r.url.endsWith('/sessions')))).flush({
      items: [session()],
      page: 1,
      pageSize: 1,
      totalCount: 2,
    });
    (
      await vi.waitFor(() =>
        http.expectOne((r) => r.url.endsWith('/sessions') && r.params.get('page') === '2'),
      )
    ).flush({ items: [{ ...session(), id: 'second-page' }], page: 2, pageSize: 1, totalCount: 2 });
    await tick();
    context.select('second-page');
    expect((await context.offlineEvidence(1, 10)).till?.sessionId).toBe('second-page');
    http.verify();
  });
});
