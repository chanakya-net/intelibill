import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { EMPTY, of } from 'rxjs';
import { beforeEach, afterEach, describe, it, expect, vi } from 'vitest';
import { AuthService } from '../../../core/auth/auth.service';
import { TillSessionPageComponent } from './till-session-page.component';
import { TillService } from '../services/till.service';
import { TillContextService } from '../services/till-context.service';
import { TillDraftService } from '../services/till-draft.service';
import { TillSession } from '../services/till.models';
const user = signal<any>({
  user: { id: 'u' },
  activeShopId: 's',
  shops: [{ shopId: 's', role: 'Owner' }],
});
const cap = signal({ enabled: true, mixedTenderEnabled: false, businessDate: '2026-11-08' });
const drawer = () =>
  ({
    id: 'd',
    tillId: 't',
    cashierId: 'u',
    cashierName: 'Cashier',
    tillName: 'Front',
    businessDate: '2026-11-08',
    state: 'Open',
    version: 2,
    openingFloat: 100,
    expectedCash: 120,
    recordedTenderTotals: { Cash: 20 },
    storeCreditIssued: 0,
    storeCreditRedeemed: 0,
    receivableChange: 0,
    latePostings: [],
    close: null,
    review: null,
    awaitingReview: false,
  }) as unknown as TillSession;
describe('TillSessionPageComponent lifecycle boundary', () => {
  const api = {
    members: () => of([{ userId: 'u', firstName: 'Cashier', lastName: 'Name', status: 'Active' }]),
    tills: () => of([{ id: 't', name: 'Front' }]),
    sessions: () => of({ items: [drawer()], totalCount: 1 }),
    detail: () =>
      of({
        session: drawer(),
        movements: { items: [], page: 1, totalCount: 0 },
        sources: { items: [], page: 1, totalCount: 0 },
      }),
    execute: vi.fn(() => of(drawer())),
  };
  beforeEach(() => {
    user.set({ user: { id: 'u' }, activeShopId: 's', shops: [{ shopId: 's', role: 'Owner' }] });
    cap.set({ enabled: true, mixedTenderEnabled: false, businessDate: '2026-11-08' });
    api.execute.mockClear();
    TestBed.configureTestingModule({
      imports: [
        TillSessionPageComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { session: user } },
        { provide: TillService, useValue: api },
        {
          provide: TillContextService,
          useValue: {
            owner: () => ({ userId: 'u', shopId: 's' }),
            capability: cap,
            enabled: () => cap().enabled,
            error: () => false,
            sessions: () => [],
            refresh: async () => undefined,
          },
        },
        { provide: TillDraftService, useValue: { list: async () => [], watch: () => EMPTY } },
      ],
    });
  });
  afterEach(() => {
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });
  async function setup() {
    const fixture = TestBed.createComponent(TillSessionPageComponent);
    fixture.detectChanges();
    await vi.waitFor(() => expect(fixture.componentInstance.busy()).toBe(false));
    const page = fixture.componentInstance;
    page.detail.set({
      session: drawer(),
      movements: { items: [], page: 1, pageSize: 20, totalCount: 0 },
      sources: { items: [], page: 1, pageSize: 20, totalCount: 0 },
    });
    return { page, fixture };
  }
  it('requires an entered opening/count while allowing explicit zero', async () => {
    const { page } = await setup();
    expect(page.openingFloat).toBeNull();
    expect(page.countedCash).toBeNull();
    page.tillId = 't';
    page.cashierId = 'u';
    page.open();
    expect(api.execute).not.toHaveBeenCalled();
    page.openingFloat = 0;
    page.open();
    expect(api.execute).toHaveBeenCalledWith(
      expect.anything(),
      'open',
      't',
      expect.anything(),
      expect.objectContaining({ openingFloat: 0 }),
      expect.anything(),
    );
  });
  it('blocks a nonzero variance without a reason', async () => {
    const { page } = await setup();
    page.countedCash = 0;
    page.close();
    expect(api.execute).not.toHaveBeenCalled();
    expect(page.error()).toBe('tills.varianceReason');
    page.reason = 'Count checked';
    page.close();
    expect(api.execute).toHaveBeenCalledWith(
      expect.anything(),
      'close',
      'd',
      'close',
      expect.objectContaining({ countedCash: 0, reason: 'Count checked', expectedVersion: 2 }),
      expect.anything(),
    );
  });
  it('cannot open or close while offline', async () => {
    const { page } = await setup();
    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false);
    page.tillId = 't';
    page.cashierId = 'u';
    page.openingFloat = 0;
    page.open();
    page.countedCash = 120;
    page.close();
    expect(api.execute).not.toHaveBeenCalled();
  });
  it('requires manager overdue finalization and its reason even when variance is zero', async () => {
    const { page } = await setup();
    page.detail.update((d) => ({ ...d!, session: { ...d!.session, businessDate: '2026-11-07' } }));
    page.countedCash = 120;
    page.close();
    expect(api.execute).not.toHaveBeenCalled();
    page.finalizeOverdue = true;
    page.reason = 'Late count witnessed';
    page.close();
    expect(api.execute).toHaveBeenCalledWith(
      expect.anything(),
      'close',
      'd',
      'close',
      expect.objectContaining({ finalizeOverdue: true, reason: 'Late count witnessed' }),
      expect.anything(),
    );
  });
  it('preserves Staff restrictions on catalog and overdue review', async () => {
    const { page } = await setup();
    user.set({ user: { id: 'u' }, activeShopId: 's', shops: [{ shopId: 's', role: 'Staff' }] });
    page.name = 'Back';
    page.create();
    page.reason = 'Reviewed';
    page.review();
    expect(api.execute).not.toHaveBeenCalled();
  });
  it('does not invent overdue status when the business-date capability is unknown', async () => {
    const { page } = await setup();
    cap.set(null!);
    expect(page.overdue(drawer())).toBe(false);
  });
});
