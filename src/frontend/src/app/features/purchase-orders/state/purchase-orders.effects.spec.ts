import { IDBFactory } from 'fake-indexeddb';
import { HttpErrorResponse } from '@angular/common/http';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { ReceivingDraftService } from '../services/receiving-draft.service';
import { ReceivingEditableDraft, ReceivingCommand } from '../services/receiving.models';
import { TestBed } from '@angular/core/testing';
import { Action } from '@ngrx/store';
import { Actions } from '@ngrx/effects';
import { Router } from '@angular/router';
import { Observable, Subject, firstValueFrom, of, throwError } from 'rxjs';
import { take } from 'rxjs/operators';
import { vi } from 'vitest';

import { PurchaseOrderDetail, PurchaseOrderService } from '../services/purchase-order.service';
import { PurchaseOrdersActions } from './purchase-orders.actions';
import { PurchaseOrdersEffects } from './purchase-orders.effects';

describe('PurchaseOrdersEffects', () => {
  let actions$: Subject<Action>;
  let effects: PurchaseOrdersEffects;

  const service = {
    getPurchaseOrders: vi.fn<PurchaseOrderService['getPurchaseOrders']>(),
    getPurchaseOrderDetail: vi.fn<PurchaseOrderService['getPurchaseOrderDetail']>(),
    createDraft: vi.fn<PurchaseOrderService['createDraft']>(),
    updateDraft: vi.fn<PurchaseOrderService['updateDraft']>(),
    placePurchaseOrder: vi.fn<PurchaseOrderService['placePurchaseOrder']>(),
    deleteDraft: vi.fn<PurchaseOrderService['deleteDraft']>(),
    cancelPurchaseOrder: vi.fn<PurchaseOrderService['cancelPurchaseOrder']>(),
    getReceiptOperation: vi.fn<PurchaseOrderService['getReceiptOperation']>(),
    receivePurchaseOrder: vi.fn<PurchaseOrderService['receivePurchaseOrder']>(),
  };

  const mockRouter = {
    navigate: vi.fn(),
  };

  beforeEach(() => {
    vi.stubGlobal('indexedDB', new IDBFactory());
    actions$ = new Subject<Action>();
    service.getPurchaseOrders.mockReset();
    service.getPurchaseOrderDetail.mockReset();
    service.createDraft.mockReset();
    service.updateDraft.mockReset();
    service.placePurchaseOrder.mockReset();
    service.deleteDraft.mockReset();
    service.cancelPurchaseOrder.mockReset();
    service.receivePurchaseOrder.mockReset();
    service.getReceiptOperation.mockReset();
    mockRouter.navigate.mockReset();

    TestBed.configureTestingModule({
      providers: [
        PurchaseOrdersEffects,
        { provide: PurchaseOrderService, useValue: service },
        { provide: Router, useValue: mockRouter },
        {
          provide: Actions,
          useFactory: (): Observable<Action> => new Actions(actions$),
        },
      ],
    });

    TestBed.inject(ShopContextService).initialize({ userId: 'u', shopId: 's' });
    effects = TestBed.inject(PurchaseOrdersEffects);
  });

  afterEach(() => {
    actions$.complete();
    TestBed.resetTestingModule();
    vi.unstubAllGlobals();
  });

  it('dispatches loadPurchaseOrdersSucceeded on load success', async () => {
    const result = {
      items: [
        {
          purchaseOrderId: 'po1',
          purchaseOrderNumber: 'PO-2026-000001',
          status: 'Draft' as const,
          supplierName: null,
          supplierReference: null,
          lineCount: 0,
          expectedQuantity: 1,
          receivedQuantity: 0,
          expectedTotal: 0,
          createdAt: '2026-06-01T00:00:00Z',
        },
      ],
      totalCount: 1,
      pageNumber: 1,
      pageSize: 20,
    };
    service.getPurchaseOrders.mockReturnValue(of(result));

    const output = firstValueFrom(effects.loadOrders$.pipe(take(1)));
    actions$.next(
      PurchaseOrdersActions.loadPurchaseOrdersRequested({ filters: { search: 'rice' } }),
    );

    await expect(output).resolves.toEqual(
      PurchaseOrdersActions.loadPurchaseOrdersSucceeded({ result }),
    );
  });

  it('dispatches loadPurchaseOrdersFailed on load error', async () => {
    service.getPurchaseOrders.mockReturnValue(throwError(() => new Error('network')));

    const output = firstValueFrom(effects.loadOrders$.pipe(take(1)));
    actions$.next(PurchaseOrdersActions.loadPurchaseOrdersRequested({ filters: {} }));

    await expect(output).resolves.toEqual(
      PurchaseOrdersActions.loadPurchaseOrdersFailed({
        errorMessage: 'purchaseOrders.errors.unableToLoad',
      }),
    );
  });

  it('maps PurchaseOrder.NotFound on detail load error', async () => {
    service.getPurchaseOrderDetail.mockReturnValue(
      throwError(() => ({ error: { title: 'PurchaseOrder.NotFound' } })),
    );

    const output = firstValueFrom(effects.loadDetail$.pipe(take(1)));
    actions$.next(
      PurchaseOrdersActions.loadPurchaseOrderDetailRequested({ purchaseOrderId: 'po1' }),
    );

    await expect(output).resolves.toEqual(
      PurchaseOrdersActions.loadPurchaseOrderDetailFailed({
        errorMessage: 'purchaseOrders.errors.notFound',
      }),
    );
  });

  it('dispatches createDraftSucceeded on success', async () => {
    const detail: PurchaseOrderDetail = {
      purchaseOrderId: 'po2',
      purchaseOrderNumber: 'PO-2026-000002',
      status: 'Draft' as const,
      supplierId: null,
      supplierName: null,
      supplierReference: null,
      receivedQuantity: 0,
      orderDate: null,
      expectedDeliveryDate: null,
      supplierReferenceNumber: null,
      notes: null,
      lines: [],
      expectedTotal: 0,
      createdAt: '2026-06-01T00:00:00Z',
      cancellationReason: null,
    };
    service.createDraft.mockReturnValue(of(detail));

    const output = firstValueFrom(effects.createDraft$.pipe(take(1)));
    actions$.next(
      PurchaseOrdersActions.createDraftRequested({
        payload: {
          supplierId: null,
          orderDate: null,
          expectedDeliveryDate: null,
          supplierReferenceNumber: null,
          notes: null,
          supplierName: null,
          supplierReference: null,
          lines: [],
        },
      }),
    );

    await expect(output).resolves.toEqual(
      PurchaseOrdersActions.createDraftSucceeded({ order: detail }),
    );
  });

  it('maps forbidden error on create failure', async () => {
    service.createDraft.mockReturnValue(
      throwError(() => ({ error: { title: 'PurchaseOrder.UserCannotCreatePurchaseOrder' } })),
    );

    const output = firstValueFrom(effects.createDraft$.pipe(take(1)));
    actions$.next(
      PurchaseOrdersActions.createDraftRequested({
        payload: {
          supplierId: null,
          orderDate: null,
          expectedDeliveryDate: null,
          supplierReferenceNumber: null,
          notes: null,
          supplierName: null,
          supplierReference: null,
          lines: [],
        },
      }),
    );

    await expect(output).resolves.toEqual(
      PurchaseOrdersActions.createDraftFailed({
        errorMessage: 'purchaseOrders.errors.forbidden',
      }),
    );
  });

  it('dispatches updateDraftSucceeded on success', async () => {
    const detail: PurchaseOrderDetail = {
      purchaseOrderId: 'po1',
      purchaseOrderNumber: 'PO-2026-000001',
      status: 'Draft' as const,
      supplierId: null,
      supplierName: null,
      supplierReference: null,
      receivedQuantity: 0,
      orderDate: null,
      expectedDeliveryDate: null,
      supplierReferenceNumber: null,
      notes: 'Updated notes',
      lines: [],
      expectedTotal: 0,
      createdAt: '2026-06-01T00:00:00Z',
      cancellationReason: null,
    };
    service.updateDraft.mockReturnValue(of(detail));

    const output = firstValueFrom(effects.updateDraft$.pipe(take(1)));
    actions$.next(
      PurchaseOrdersActions.updateDraftRequested({
        purchaseOrderId: 'po1',
        payload: {
          supplierId: null,
          orderDate: null,
          expectedDeliveryDate: null,
          supplierReferenceNumber: null,
          notes: 'Updated notes',
          lines: [],
        },
      }),
    );

    await expect(output).resolves.toEqual(
      PurchaseOrdersActions.updateDraftSucceeded({ order: detail }),
    );
  });

  it('maps cannotUpdateNonDraft error on update failure', async () => {
    service.updateDraft.mockReturnValue(
      throwError(() => ({ error: { title: 'PurchaseOrder.CannotUpdateNonDraft' } })),
    );

    const output = firstValueFrom(effects.updateDraft$.pipe(take(1)));
    actions$.next(
      PurchaseOrdersActions.updateDraftRequested({
        purchaseOrderId: 'po1',
        payload: {
          supplierId: null,
          orderDate: null,
          expectedDeliveryDate: null,
          supplierReferenceNumber: null,
          notes: null,
          lines: [],
        },
      }),
    );

    await expect(output).resolves.toEqual(
      PurchaseOrdersActions.updateDraftFailed({
        errorMessage: 'purchaseOrders.errors.cannotUpdateNonDraft',
      }),
    );
  });

  it('dispatches placeOrderSucceeded on success', async () => {
    const detail: PurchaseOrderDetail = {
      purchaseOrderId: 'po1',
      purchaseOrderNumber: 'PO-2026-000001',
      status: 'Placed' as const,
      supplierId: 'sup1',
      supplierName: 'Acme Traders',
      supplierReference: 'SUP-REF-001',
      receivedQuantity: 0,
      orderDate: null,
      expectedDeliveryDate: null,
      supplierReferenceNumber: null,
      notes: null,
      lines: [],
      expectedTotal: 0,
      createdAt: '2026-06-01T00:00:00Z',
      cancellationReason: null,
    };
    service.placePurchaseOrder.mockReturnValue(of(detail));

    const output = firstValueFrom(effects.placeOrder$.pipe(take(1)));
    actions$.next(PurchaseOrdersActions.placeOrderRequested({ purchaseOrderId: 'po1' }));

    await expect(output).resolves.toEqual(
      PurchaseOrdersActions.placeOrderSucceeded({ order: detail }),
    );
  });

  it('maps SupplierRequired error on place failure', async () => {
    service.placePurchaseOrder.mockReturnValue(
      throwError(() => ({ error: { title: 'PurchaseOrder.SupplierRequired' } })),
    );

    const output = firstValueFrom(effects.placeOrder$.pipe(take(1)));
    actions$.next(PurchaseOrdersActions.placeOrderRequested({ purchaseOrderId: 'po1' }));

    await expect(output).resolves.toEqual(
      PurchaseOrdersActions.placeOrderFailed({
        errorMessage: 'purchaseOrders.errors.supplierRequired',
      }),
    );
  });

  it('dispatches deleteDraftSucceeded on success', async () => {
    service.deleteDraft.mockReturnValue(of(undefined as unknown as void));

    const output = firstValueFrom(effects.deleteDraft$.pipe(take(1)));
    actions$.next(PurchaseOrdersActions.deleteDraftRequested({ purchaseOrderId: 'po1' }));

    await expect(output).resolves.toEqual(
      PurchaseOrdersActions.deleteDraftSucceeded({ purchaseOrderId: 'po1' }),
    );
  });

  it('maps CannotDeleteNonDraft error on delete failure', async () => {
    service.deleteDraft.mockReturnValue(
      throwError(() => ({ error: { title: 'PurchaseOrder.CannotDeleteNonDraft' } })),
    );

    const output = firstValueFrom(effects.deleteDraft$.pipe(take(1)));
    actions$.next(PurchaseOrdersActions.deleteDraftRequested({ purchaseOrderId: 'po1' }));

    await expect(output).resolves.toEqual(
      PurchaseOrdersActions.deleteDraftFailed({
        errorMessage: 'purchaseOrders.errors.cannotDeleteNonDraft',
      }),
    );
  });

  it('dispatches cancelOrderSucceeded on success', async () => {
    const detail: PurchaseOrderDetail = {
      purchaseOrderId: 'po1',
      purchaseOrderNumber: 'PO-2026-000001',
      status: 'Cancelled' as const,
      supplierId: null,
      supplierName: null,
      supplierReference: null,
      receivedQuantity: 0,
      orderDate: null,
      expectedDeliveryDate: null,
      supplierReferenceNumber: null,
      notes: null,
      lines: [],
      expectedTotal: 0,
      createdAt: '2026-06-01T00:00:00Z',
      cancellationReason: 'Supplier unavailable',
    };
    service.cancelPurchaseOrder.mockReturnValue(of(detail));

    const output = firstValueFrom(effects.cancelOrder$.pipe(take(1)));
    actions$.next(
      PurchaseOrdersActions.cancelOrderRequested({
        purchaseOrderId: 'po1',
        payload: { reason: 'Supplier unavailable' },
      }),
    );

    await expect(output).resolves.toEqual(
      PurchaseOrdersActions.cancelOrderSucceeded({ order: detail }),
    );
  });

  it('maps CannotCancelAfterReceipt error on cancel failure', async () => {
    service.cancelPurchaseOrder.mockReturnValue(
      throwError(() => ({ error: { title: 'PurchaseOrder.CannotCancelAfterReceipt' } })),
    );

    const output = firstValueFrom(effects.cancelOrder$.pipe(take(1)));
    actions$.next(
      PurchaseOrdersActions.cancelOrderRequested({
        purchaseOrderId: 'po1',
        payload: { reason: null },
      }),
    );

    await expect(output).resolves.toEqual(
      PurchaseOrdersActions.cancelOrderFailed({
        errorMessage: 'purchaseOrders.errors.cannotCancelAfterReceipt',
      }),
    );
  });

  it('maps CancellationReasonRequired error on cancel failure', async () => {
    service.cancelPurchaseOrder.mockReturnValue(
      throwError(() => ({ error: { title: 'PurchaseOrder.CancellationReasonRequired' } })),
    );

    const output = firstValueFrom(effects.cancelOrder$.pipe(take(1)));
    actions$.next(
      PurchaseOrdersActions.cancelOrderRequested({
        purchaseOrderId: 'po1',
        payload: { reason: null },
      }),
    );

    await expect(output).resolves.toEqual(
      PurchaseOrdersActions.cancelOrderFailed({
        errorMessage: 'purchaseOrders.errors.cancellationReasonRequired',
      }),
    );
  });

  const draft: ReceivingEditableDraft = {
    referenceNumber: '',
    notes: '',
    receivedAt: null,
    lines: [
      {
        clientLineId: 'row-1',
        purchaseOrderLineId: 'line-1',
        barcode: 'product',
        batchNumber: 'lot A',
        quantity: 4,
        totalPurchaseCost: 20,
        mrp: 8,
        salesPrice: 7,
        taxRatePercent: 5,
        taxIncluded: true,
        purchaseTaxIncluded: false,
        expiryDate: null,
        manufacturingDate: null,
      },
    ],
  };
  async function prepare(poId = 'po1'): Promise<ReceivingCommand> {
    const context = TestBed.inject(ShopContextService);
    const record = await TestBed.inject(ReceivingDraftService).prepare(
      context.scope()!,
      poId,
      draft,
      0,
    );
    return {
      correlation: {
        scope: context.scope()!,
        purchaseOrderId: poId,
        operationId: record.pending!.operationId,
      },
      payload: record.pending!.payload,
      recovery: false,
    };
  }
  const response = (purchaseOrderId = 'po1') => ({ purchaseOrderId }) as PurchaseOrderDetail;

  it('confirms under the captured owner before announcing success, with scoped HTTP context', async () => {
    const command = await prepare();
    service.receivePurchaseOrder.mockReturnValue(of(response()));
    const output = firstValueFrom(effects.receiveOrder$);
    actions$.next(PurchaseOrdersActions.receiveOrderRequested(command));
    expect(TestBed.inject(ShopContextService).hasMutationsInFlight()).toBe(true);
    await expect(output).resolves.toEqual(
      PurchaseOrdersActions.receiveOrderSucceeded({ correlation: command.correlation }),
    );
    const stored = await TestBed.inject(ReceivingDraftService).load(
      command.correlation.scope,
      'po1',
    );
    expect(stored!.confirmedOperationId).toBe(command.correlation.operationId);
    expect(service.receivePurchaseOrder).toHaveBeenCalledWith(
      'po1',
      command.payload,
      command.correlation.scope,
    );
    expect(TestBed.inject(ShopContextService).hasMutationsInFlight()).toBe(false);
  });

  it('retains editable fields and inline errors after definitive validation rejection', async () => {
    const command = await prepare();
    service.receivePurchaseOrder.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: {
              title: 'Inventory.BatchNumberAlreadyExists',
              rowErrors: [
                { code: 'Inventory.BatchNumberAlreadyExists', lineIndex: 0, field: 'BatchNumber' },
              ],
            },
          }),
      ),
    );
    const output = firstValueFrom(effects.receiveOrder$);
    actions$.next(PurchaseOrdersActions.receiveOrderRequested(command));
    expect(await output).toEqual(
      PurchaseOrdersActions.receiveOrderFailed({
        correlation: command.correlation,
        outcome: 'rejected',
        errorMessage: 'purchaseOrders.errors.batchNumberAlreadyExists',
        rowErrors: [expect.objectContaining({ clientLineId: 'row-1', field: 'batchNumber' })],
      }),
    );
    const stored = await TestBed.inject(ReceivingDraftService).load(
      command.correlation.scope,
      'po1',
    );
    expect(stored!.pending).toBeNull();
    expect(stored!.draft).toEqual(draft);
  });

  it('preserves a lost response through expired recovery authority, then resolves by the same-key lookup', async () => {
    const command = await prepare();
    service.receivePurchaseOrder.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 0 })),
    );
    let output = firstValueFrom(effects.receiveOrder$);
    actions$.next(PurchaseOrdersActions.receiveOrderRequested(command));
    expect('outcome' in (await output) ? ((await output) as { outcome: string }).outcome : '').toBe(
      'unknown',
    );
    service.receivePurchaseOrder.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 401 })),
    );
    output = firstValueFrom(effects.receiveOrder$);
    actions$.next(PurchaseOrdersActions.receiveOrderRequested({ ...command, recovery: true }));
    expect('outcome' in (await output) ? ((await output) as { outcome: string }).outcome : '').toBe(
      'unknown',
    );
    service.getReceiptOperation.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 403 })),
    );
    let check = firstValueFrom(effects.checkReceipt$);
    actions$.next(
      PurchaseOrdersActions.checkReceiptRequested({ correlation: command.correlation }),
    );
    expect((await check).type).toBe(PurchaseOrdersActions.checkReceiptFailed.type);
    expect(
      (await TestBed.inject(ReceivingDraftService).load(command.correlation.scope, 'po1'))!.pending!
        .payload,
    ).toEqual(command.payload);
    service.getReceiptOperation.mockReturnValue(
      of({
        operationType: 'purchase-order-receipt',
        operationKey: command.correlation.operationId,
        targetId: 'po1',
        response: response(),
      }),
    );
    check = firstValueFrom(effects.checkReceipt$);
    actions$.next(
      PurchaseOrdersActions.checkReceiptRequested({ correlation: command.correlation }),
    );
    expect(await check).toEqual(
      PurchaseOrdersActions.checkReceiptSucceeded({ correlation: command.correlation }),
    );
    expect(
      (await TestBed.inject(ReceivingDraftService).load(command.correlation.scope, 'po1'))!
        .confirmedOperationId,
    ).toBe(command.correlation.operationId);
  });

  it('does not cancel a receive, ignores in-flight duplicates and allows independent targets', async () => {
    const a = await prepare();
    const b = await prepare('po2');
    const first = new Subject<PurchaseOrderDetail>();
    const second = new Subject<PurchaseOrderDetail>();
    service.receivePurchaseOrder.mockImplementation((id) => (id === 'po1' ? first : second));
    const results: unknown[] = [];
    const subscription = effects.receiveOrder$.subscribe((result) => results.push(result));
    actions$.next(PurchaseOrdersActions.receiveOrderRequested(a));
    actions$.next(PurchaseOrdersActions.receiveOrderRequested(a));
    actions$.next(PurchaseOrdersActions.receiveOrderRequested(b));
    await vi.waitFor(() => expect(service.receivePurchaseOrder).toHaveBeenCalledTimes(2));
    first.next(response());
    second.next(response('po2'));
    await vi.waitFor(() => expect(results).toHaveLength(2));
    subscription.unsubscribe();
  });

  it('refuses a foreign send; a late response remains unknown under its original owner without repopulating state', async () => {
    const command = await prepare();
    const pending = new Subject<PurchaseOrderDetail>();
    service.receivePurchaseOrder.mockReturnValue(pending);
    const results: unknown[] = [];
    const subscription = effects.receiveOrder$.subscribe((result) => results.push(result));
    actions$.next(PurchaseOrdersActions.receiveOrderRequested(command));
    await vi.waitFor(() => expect(service.receivePurchaseOrder).toHaveBeenCalledOnce());
    const context = TestBed.inject(ShopContextService);
    context.transition(null, 'sign-out', () => {});
    pending.next(response());
    await vi.waitFor(() => expect(context.hasMutationsInFlight()).toBe(false));
    expect(results).toEqual([]);
    expect(
      (await TestBed.inject(ReceivingDraftService).load(command.correlation.scope, 'po1'))!.pending!
        .status,
    ).toBe('unknown');
    actions$.next(PurchaseOrdersActions.receiveOrderRequested(command));
    await vi.waitFor(() => expect(context.hasMutationsInFlight()).toBe(false));
    expect(service.receivePurchaseOrder).toHaveBeenCalledOnce();
    subscription.unsubscribe();
  });

  it('leaves a lookup miss or mismatched identity unresolved', async () => {
    const command = await prepare();
    service.getReceiptOperation.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 404 })),
    );
    let output = firstValueFrom(effects.checkReceipt$);
    actions$.next(
      PurchaseOrdersActions.checkReceiptRequested({ correlation: command.correlation }),
    );
    expect(await output).toEqual(
      PurchaseOrdersActions.checkReceiptMissing({ correlation: command.correlation }),
    );
    service.getReceiptOperation.mockReturnValue(
      of({
        operationType: 'purchase-order-receipt',
        operationKey: 'other',
        targetId: 'po1',
        response: response(),
      }),
    );
    output = firstValueFrom(effects.checkReceipt$);
    actions$.next(
      PurchaseOrdersActions.checkReceiptRequested({ correlation: command.correlation }),
    );
    expect((await output).type).toBe(PurchaseOrdersActions.checkReceiptFailed.type);
    expect(
      (await TestBed.inject(ReceivingDraftService).load(command.correlation.scope, 'po1'))!.pending!
        .operationId,
    ).toBe(command.correlation.operationId);
  });

  it('navigates to list page on deleteDraftSucceeded', () => {
    effects.deleteDraftSucceeded$.subscribe();
    actions$.next(PurchaseOrdersActions.deleteDraftSucceeded({ purchaseOrderId: 'po1' }));
    expect(mockRouter.navigate).toHaveBeenCalledWith(['/inventory/purchase-orders']);
  });
});
