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
    receivePurchaseOrder: vi.fn<PurchaseOrderService['receivePurchaseOrder']>(),
  };

  const mockRouter = {
    navigate: vi.fn(),
  };

  beforeEach(() => {
    actions$ = new Subject<Action>();
    service.getPurchaseOrders.mockReset();
    service.getPurchaseOrderDetail.mockReset();
    service.createDraft.mockReset();
    service.updateDraft.mockReset();
    service.placePurchaseOrder.mockReset();
    service.deleteDraft.mockReset();
    service.cancelPurchaseOrder.mockReset();
    service.receivePurchaseOrder.mockReset();
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

    effects = TestBed.inject(PurchaseOrdersEffects);
  });

  afterEach(() => {
    actions$.complete();
    TestBed.resetTestingModule();
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
    actions$.next(PurchaseOrdersActions.loadPurchaseOrdersRequested({ filters: { search: 'rice' } }));

    await expect(output).resolves.toEqual(
      PurchaseOrdersActions.loadPurchaseOrdersSucceeded({ result })
    );
  });

  it('dispatches loadPurchaseOrdersFailed on load error', async () => {
    service.getPurchaseOrders.mockReturnValue(throwError(() => new Error('network')));

    const output = firstValueFrom(effects.loadOrders$.pipe(take(1)));
    actions$.next(PurchaseOrdersActions.loadPurchaseOrdersRequested({ filters: {} }));

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
      })
    );

    await expect(output).resolves.toEqual(PurchaseOrdersActions.createDraftSucceeded({ order: detail }));
  });

  it('maps forbidden error on create failure', async () => {
    service.createDraft.mockReturnValue(
      throwError(() => ({ error: { title: 'PurchaseOrder.UserCannotCreatePurchaseOrder' } }))
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
      })
    );

    await expect(output).resolves.toEqual(
      PurchaseOrdersActions.createDraftFailed({
        errorMessage: 'purchaseOrders.errors.forbidden',
      })
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
    actions$.next(PurchaseOrdersActions.updateDraftRequested({ purchaseOrderId: 'po1', payload: { supplierId: null, orderDate: null, expectedDeliveryDate: null, supplierReferenceNumber: null, notes: 'Updated notes', lines: [] } }));

    await expect(output).resolves.toEqual(PurchaseOrdersActions.updateDraftSucceeded({ order: detail }));
  });

  it('maps cannotUpdateNonDraft error on update failure', async () => {
    service.updateDraft.mockReturnValue(
      throwError(() => ({ error: { title: 'PurchaseOrder.CannotUpdateNonDraft' } }))
    );

    const output = firstValueFrom(effects.updateDraft$.pipe(take(1)));
    actions$.next(PurchaseOrdersActions.updateDraftRequested({ purchaseOrderId: 'po1', payload: { supplierId: null, orderDate: null, expectedDeliveryDate: null, supplierReferenceNumber: null, notes: null, lines: [] } }));

    await expect(output).resolves.toEqual(
      PurchaseOrdersActions.updateDraftFailed({
        errorMessage: 'purchaseOrders.errors.cannotUpdateNonDraft',
      })
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

    await expect(output).resolves.toEqual(PurchaseOrdersActions.placeOrderSucceeded({ order: detail }));
  });

  it('maps SupplierRequired error on place failure', async () => {
    service.placePurchaseOrder.mockReturnValue(
      throwError(() => ({ error: { title: 'PurchaseOrder.SupplierRequired' } }))
    );

    const output = firstValueFrom(effects.placeOrder$.pipe(take(1)));
    actions$.next(PurchaseOrdersActions.placeOrderRequested({ purchaseOrderId: 'po1' }));

    await expect(output).resolves.toEqual(
      PurchaseOrdersActions.placeOrderFailed({
        errorMessage: 'purchaseOrders.errors.supplierRequired',
      })
    );
  });

  it('dispatches deleteDraftSucceeded on success', async () => {
    service.deleteDraft.mockReturnValue(of(undefined as unknown as void));

    const output = firstValueFrom(effects.deleteDraft$.pipe(take(1)));
    actions$.next(PurchaseOrdersActions.deleteDraftRequested({ purchaseOrderId: 'po1' }));

    await expect(output).resolves.toEqual(PurchaseOrdersActions.deleteDraftSucceeded({ purchaseOrderId: 'po1' }));
  });

  it('maps CannotDeleteNonDraft error on delete failure', async () => {
    service.deleteDraft.mockReturnValue(
      throwError(() => ({ error: { title: 'PurchaseOrder.CannotDeleteNonDraft' } }))
    );

    const output = firstValueFrom(effects.deleteDraft$.pipe(take(1)));
    actions$.next(PurchaseOrdersActions.deleteDraftRequested({ purchaseOrderId: 'po1' }));

    await expect(output).resolves.toEqual(
      PurchaseOrdersActions.deleteDraftFailed({
        errorMessage: 'purchaseOrders.errors.cannotDeleteNonDraft',
      })
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
    actions$.next(PurchaseOrdersActions.cancelOrderRequested({ purchaseOrderId: 'po1', payload: { reason: 'Supplier unavailable' } }));

    await expect(output).resolves.toEqual(PurchaseOrdersActions.cancelOrderSucceeded({ order: detail }));
  });

  it('maps CannotCancelAfterReceipt error on cancel failure', async () => {
    service.cancelPurchaseOrder.mockReturnValue(
      throwError(() => ({ error: { title: 'PurchaseOrder.CannotCancelAfterReceipt' } }))
    );

    const output = firstValueFrom(effects.cancelOrder$.pipe(take(1)));
    actions$.next(PurchaseOrdersActions.cancelOrderRequested({ purchaseOrderId: 'po1', payload: { reason: null } }));

    await expect(output).resolves.toEqual(
      PurchaseOrdersActions.cancelOrderFailed({
        errorMessage: 'purchaseOrders.errors.cannotCancelAfterReceipt',
      })
    );
  });

  it('maps CancellationReasonRequired error on cancel failure', async () => {
    service.cancelPurchaseOrder.mockReturnValue(
      throwError(() => ({ error: { title: 'PurchaseOrder.CancellationReasonRequired' } }))
    );

    const output = firstValueFrom(effects.cancelOrder$.pipe(take(1)));
    actions$.next(PurchaseOrdersActions.cancelOrderRequested({ purchaseOrderId: 'po1', payload: { reason: null } }));

    await expect(output).resolves.toEqual(
      PurchaseOrdersActions.cancelOrderFailed({
        errorMessage: 'purchaseOrders.errors.cancellationReasonRequired',
      })
    );
  });

  it('dispatches receiveOrderSucceeded on receive success', async () => {
    const detail: PurchaseOrderDetail = {
      purchaseOrderId: 'po1',
      purchaseOrderNumber: 'PO-2026-000001',
      status: 'PartiallyReceived' as const,
      supplierId: null,
      supplierName: null,
      supplierReference: null,
      receivedQuantity: 2,
      orderDate: null,
      expectedDeliveryDate: null,
      supplierReferenceNumber: null,
      notes: null,
      lines: [],
      expectedTotal: 100,
      createdAt: '2026-06-01T00:00:00Z',
      cancellationReason: null,
    };
    const payload = {
      referenceNumber: null,
      notes: null,
      receivedAt: null,
      lines: [{
        purchaseOrderLineId: 'line-1',
        barcode: 'IT-PO-001',
        batchNumber: 'BATCH-1',
        quantity: 2,
        totalPurchaseCost: 100,
        mrp: 70,
        salesPrice: 60,
        taxRatePercent: 5,
        taxIncluded: false,
        purchaseTaxIncluded: true,
        expiryDate: null,
        manufacturingDate: null,
      }],
    };
    service.receivePurchaseOrder.mockReturnValue(of(detail));

    const output = firstValueFrom(effects.receiveOrder$.pipe(take(1)));
    actions$.next(PurchaseOrdersActions.receiveOrderRequested({ purchaseOrderId: 'po1', payload }));

    await expect(output).resolves.toEqual(PurchaseOrdersActions.receiveOrderSucceeded({ order: detail }));
    expect(service.receivePurchaseOrder).toHaveBeenCalledWith('po1', payload);
  });

  it('maps receive errors to purchase-order receive error keys', async () => {
    service.receivePurchaseOrder.mockReturnValue(
      throwError(() => ({ error: { title: 'Inventory.BatchNumberAlreadyExists' } }))
    );

    const output = firstValueFrom(effects.receiveOrder$.pipe(take(1)));
    actions$.next(
      PurchaseOrdersActions.receiveOrderRequested({
        purchaseOrderId: 'po1',
        payload: {
          referenceNumber: null,
          notes: null,
          receivedAt: null,
          lines: [{
            purchaseOrderLineId: 'line-1',
            barcode: 'IT-PO-001',
            batchNumber: 'BATCH-1',
            quantity: 1,
            totalPurchaseCost: 10,
            mrp: 12,
            salesPrice: 11,
            taxRatePercent: 0,
            taxIncluded: false,
            purchaseTaxIncluded: false,
            expiryDate: null,
            manufacturingDate: null,
          }],
        },
      })
    );

    await expect(output).resolves.toEqual(
      PurchaseOrdersActions.receiveOrderFailed({
        errorMessage: 'purchaseOrders.errors.batchNumberAlreadyExists',
      })
    );
  });

  it('navigates to list page on deleteDraftSucceeded', () => {
    effects.deleteDraftSucceeded$.subscribe();
    actions$.next(PurchaseOrdersActions.deleteDraftSucceeded({ purchaseOrderId: 'po1' }));
    expect(mockRouter.navigate).toHaveBeenCalledWith(['/inventory/purchase-orders']);
  });
});
