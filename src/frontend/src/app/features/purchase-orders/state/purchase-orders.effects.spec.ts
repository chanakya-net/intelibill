import { TestBed } from '@angular/core/testing';
import { Action } from '@ngrx/store';
import { Actions } from '@ngrx/effects';
import { Observable, Subject, firstValueFrom, of, throwError } from 'rxjs';
import { take } from 'rxjs/operators';
import { vi } from 'vitest';

import { PurchaseOrderService } from '../services/purchase-order.service';
import { PurchaseOrdersActions } from './purchase-orders.actions';
import { PurchaseOrdersEffects } from './purchase-orders.effects';

describe('PurchaseOrdersEffects', () => {
  let actions$: Subject<Action>;
  let effects: PurchaseOrdersEffects;

  const service = {
    getPurchaseOrders: vi.fn<PurchaseOrderService['getPurchaseOrders']>(),
    getPurchaseOrderDetail: vi.fn<PurchaseOrderService['getPurchaseOrderDetail']>(),
    createDraft: vi.fn<PurchaseOrderService['createDraft']>(),
  };

  beforeEach(() => {
    actions$ = new Subject<Action>();
    service.getPurchaseOrders.mockReset();
    service.getPurchaseOrderDetail.mockReset();
    service.createDraft.mockReset();

    TestBed.configureTestingModule({
      providers: [
        PurchaseOrdersEffects,
        { provide: PurchaseOrderService, useValue: service },
        {
          provide: Actions,
          useFactory: (): Observable<Action> => new Actions(actions$),
        },
      ],
    });

    effects = TestBed.inject(PurchaseOrdersEffects);
  });

  afterEach(() => {
    actions$.complete();
    TestBed.resetTestingModule();
  });

  it('dispatches loadPurchaseOrdersSucceeded on load success', async () => {
    const list = [
      {
        purchaseOrderId: 'po1',
        purchaseOrderNumber: 'PO-2026-000001',
        status: 'Draft' as const,
        lineCount: 0,
        expectedTotal: 0,
        createdAt: '2026-06-01T00:00:00Z',
      },
    ];
    service.getPurchaseOrders.mockReturnValue(of(list));

    const output = firstValueFrom(effects.loadOrders$.pipe(take(1)));
    actions$.next(PurchaseOrdersActions.loadPurchaseOrdersRequested());

    await expect(output).resolves.toEqual(
      PurchaseOrdersActions.loadPurchaseOrdersSucceeded({ orders: list })
    );
  });

  it('dispatches loadPurchaseOrdersFailed on load error', async () => {
    service.getPurchaseOrders.mockReturnValue(throwError(() => new Error('network')));

    const output = firstValueFrom(effects.loadOrders$.pipe(take(1)));
    actions$.next(PurchaseOrdersActions.loadPurchaseOrdersRequested());

    await expect(output).resolves.toEqual(
      PurchaseOrdersActions.loadPurchaseOrdersFailed({
        errorMessage: 'purchaseOrders.errors.unableToLoad',
      })
    );
  });

  it('maps PurchaseOrder.NotFound on detail load error', async () => {
    service.getPurchaseOrderDetail.mockReturnValue(
      throwError(() => ({ error: { title: 'PurchaseOrder.NotFound' } }))
    );

    const output = firstValueFrom(effects.loadDetail$.pipe(take(1)));
    actions$.next(PurchaseOrdersActions.loadPurchaseOrderDetailRequested({ purchaseOrderId: 'po1' }));

    await expect(output).resolves.toEqual(
      PurchaseOrdersActions.loadPurchaseOrderDetailFailed({
        errorMessage: 'purchaseOrders.errors.notFound',
      })
    );
  });

  it('dispatches createDraftSucceeded on success', async () => {
    const detail = {
      purchaseOrderId: 'po2',
      purchaseOrderNumber: 'PO-2026-000002',
      status: 'Draft' as const,
      notes: null,
      lines: [],
      expectedTotal: 0,
      createdAt: '2026-06-01T00:00:00Z',
    };
    service.createDraft.mockReturnValue(of(detail));

    const output = firstValueFrom(effects.createDraft$.pipe(take(1)));
    actions$.next(PurchaseOrdersActions.createDraftRequested({ payload: { notes: null, lines: [] } }));

    await expect(output).resolves.toEqual(PurchaseOrdersActions.createDraftSucceeded({ order: detail }));
  });

  it('maps forbidden error on create failure', async () => {
    service.createDraft.mockReturnValue(
      throwError(() => ({ error: { title: 'PurchaseOrder.UserCannotCreatePurchaseOrder' } }))
    );

    const output = firstValueFrom(effects.createDraft$.pipe(take(1)));
    actions$.next(PurchaseOrdersActions.createDraftRequested({ payload: { notes: null, lines: [] } }));

    await expect(output).resolves.toEqual(
      PurchaseOrdersActions.createDraftFailed({
        errorMessage: 'purchaseOrders.errors.forbidden',
      })
    );
  });
});
