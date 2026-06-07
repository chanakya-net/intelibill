import { purchaseOrdersReducer, PurchaseOrdersState, purchaseOrdersAdapter } from './purchase-orders.reducer';
import { PurchaseOrdersActions } from './purchase-orders.actions';
import {
  DEFAULT_PURCHASE_ORDER_LIST_FILTERS,
  PurchaseOrderDetail,
  PurchaseOrderListItem,
} from '../services/purchase-order.service';

const initialState: PurchaseOrdersState = purchaseOrdersAdapter.getInitialState({
  loadingList: false,
  loadingDetail: false,
  submitting: false,
  errorMessage: '',
  selectedOrder: null,
  createSucceeded: false,
  totalCount: 0,
  currentPage: 1,
  pageSize: 20,
  filters: DEFAULT_PURCHASE_ORDER_LIST_FILTERS,
});

const mockListItem: PurchaseOrderListItem = {
  purchaseOrderId: 'po1',
  purchaseOrderNumber: 'PO-2026-000001',
  status: 'Draft',
  supplierName: null,
  supplierReference: null,
  lineCount: 1,
  expectedQuantity: 1,
  receivedQuantity: 0,
  expectedTotal: 100,
  createdAt: '2026-06-01T00:00:00Z',
};

const mockDetail: PurchaseOrderDetail = {
  purchaseOrderId: 'po1',
  purchaseOrderNumber: 'PO-2026-000001',
  status: 'Draft',
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

describe('purchaseOrdersReducer', () => {
  it('sets loadingList on load requested', () => {
    const next = purchaseOrdersReducer(
      initialState,
      PurchaseOrdersActions.loadPurchaseOrdersRequested({ filters: { search: 'rice', page: 1 } })
    );
    expect(next.loadingList).toBe(true);
    expect(next.errorMessage).toBe('');
    expect(next.filters.search).toBe('rice');
  });

  it('populates list on load succeeded', () => {
    const next = purchaseOrdersReducer(
      { ...initialState, loadingList: true },
      PurchaseOrdersActions.loadPurchaseOrdersSucceeded({
        result: { items: [mockListItem], totalCount: 1, pageNumber: 2, pageSize: 50 },
      })
    );
    expect(next.loadingList).toBe(false);
    const ids = next.ids as string[];
    expect(ids).toContain('po1');
    expect(next.totalCount).toBe(1);
    expect(next.currentPage).toBe(2);
    expect(next.pageSize).toBe(50);
  });

  it('preserves backend ordering on load succeeded', () => {
    const olderOpen = {
      ...mockListItem,
      purchaseOrderId: 'po-open',
      status: 'Draft' as const,
      createdAt: '2026-06-01T00:00:00Z',
    };
    const newerClosedLikeItem = {
      ...mockListItem,
      purchaseOrderId: 'po-closed',
      status: 'Draft' as const,
      createdAt: '2026-06-02T00:00:00Z',
    };

    const next = purchaseOrdersReducer(
      { ...initialState, loadingList: true },
      PurchaseOrdersActions.loadPurchaseOrdersSucceeded({
        result: {
          items: [olderOpen, newerClosedLikeItem],
          totalCount: 2,
          pageNumber: 1,
          pageSize: 20,
        },
      })
    );

    expect(next.ids).toEqual(['po-open', 'po-closed']);
  });

  it('sets error on load failed', () => {
    const next = purchaseOrdersReducer(
      initialState,
      PurchaseOrdersActions.loadPurchaseOrdersFailed({ errorMessage: 'err' })
    );
    expect(next.errorMessage).toBe('err');
    expect(next.loadingList).toBe(false);
  });

  it('sets loadingDetail on detail requested', () => {
    const next = purchaseOrdersReducer(
      initialState,
      PurchaseOrdersActions.loadPurchaseOrderDetailRequested({ purchaseOrderId: 'po1' })
    );
    expect(next.loadingDetail).toBe(true);
    expect(next.selectedOrder).toBeNull();
  });

  it('sets selectedOrder on detail succeeded', () => {
    const next = purchaseOrdersReducer(
      { ...initialState, loadingDetail: true },
      PurchaseOrdersActions.loadPurchaseOrderDetailSucceeded({ order: mockDetail })
    );
    expect(next.loadingDetail).toBe(false);
    expect(next.selectedOrder).toEqual(mockDetail);
  });

  it('sets createSucceeded on create succeeded', () => {
    const next = purchaseOrdersReducer(
      { ...initialState, submitting: true },
      PurchaseOrdersActions.createDraftSucceeded({ order: mockDetail })
    );
    expect(next.submitting).toBe(false);
    expect(next.createSucceeded).toBe(true);
    expect(next.selectedOrder).toEqual(mockDetail);
  });

  it('sets submitting on update requested', () => {
    const next = purchaseOrdersReducer(
      initialState,
      PurchaseOrdersActions.updateDraftRequested({ purchaseOrderId: 'po1', payload: { supplierId: null, orderDate: null, expectedDeliveryDate: null, supplierReferenceNumber: null, notes: 'Updated', lines: [] } })
    );
    expect(next.submitting).toBe(true);
  });

  it('updates selectedOrder and upserts order in adapter list on update succeeded', () => {
    const updatedDetail: PurchaseOrderDetail = { ...mockDetail, notes: 'Updated notes' };
    const next = purchaseOrdersReducer(
      { ...initialState, submitting: true },
      PurchaseOrdersActions.updateDraftSucceeded({ order: updatedDetail })
    );
    expect(next.submitting).toBe(false);
    expect(next.selectedOrder).toEqual(updatedDetail);
    expect(next.entities['po1']).toBeDefined();
    expect(next.entities['po1']?.expectedTotal).toBe(0);
  });

  it('clears detail on clearDetail', () => {
    const next = purchaseOrdersReducer(
      { ...initialState, selectedOrder: mockDetail },
      PurchaseOrdersActions.clearDetail()
    );
    expect(next.selectedOrder).toBeNull();
  });

  it('resets list filters', () => {
    const next = purchaseOrdersReducer(
      {
        ...initialState,
        filters: {
          search: 'rice',
          status: 'Draft',
          orderDateFrom: '2026-06-01',
          orderDateTo: '2026-06-02',
          page: 3,
          pageSize: 50,
        },
      },
      PurchaseOrdersActions.resetListFilters()
    );

    expect(next.filters).toEqual(DEFAULT_PURCHASE_ORDER_LIST_FILTERS);
  });

  it('sets submitting on place requested', () => {
    const next = purchaseOrdersReducer(
      initialState,
      PurchaseOrdersActions.placeOrderRequested({ purchaseOrderId: 'po1' })
    );
    expect(next.submitting).toBe(true);
    expect(next.errorMessage).toBe('');
  });

  it('updates selectedOrder and upserts in adapter on place succeeded', () => {
    const placedDetail: PurchaseOrderDetail = { ...mockDetail, status: 'Placed', cancellationReason: null };
    const next = purchaseOrdersReducer(
      { ...initialState, submitting: true },
      PurchaseOrdersActions.placeOrderSucceeded({ order: placedDetail })
    );
    expect(next.submitting).toBe(false);
    expect(next.selectedOrder?.status).toBe('Placed');
    expect(next.entities['po1']).toBeDefined();
  });

  it('sets error on place failed', () => {
    const next = purchaseOrdersReducer(
      { ...initialState, submitting: true },
      PurchaseOrdersActions.placeOrderFailed({ errorMessage: 'err.place' })
    );
    expect(next.submitting).toBe(false);
    expect(next.errorMessage).toBe('err.place');
  });

  it('removes entity and clears selectedOrder on delete succeeded', () => {
    const stateWithPo = purchaseOrdersAdapter.setOne(mockListItem, {
      ...initialState,
      selectedOrder: mockDetail,
      submitting: true,
    });
    const next = purchaseOrdersReducer(stateWithPo, PurchaseOrdersActions.deleteDraftSucceeded({ purchaseOrderId: 'po1' }));
    expect(next.submitting).toBe(false);
    expect(next.selectedOrder).toBeNull();
    expect(next.ids).not.toContain('po1');
  });

  it('updates selectedOrder on cancel succeeded', () => {
    const cancelledDetail: PurchaseOrderDetail = { ...mockDetail, status: 'Cancelled', cancellationReason: 'Too late', };
    const next = purchaseOrdersReducer(
      { ...initialState, submitting: true },
      PurchaseOrdersActions.cancelOrderSucceeded({ order: cancelledDetail })
    );
    expect(next.submitting).toBe(false);
    expect(next.selectedOrder?.status).toBe('Cancelled');
    expect(next.selectedOrder?.cancellationReason).toBe('Too late');
  });

  it('sets submitting on receive requested', () => {
    const next = purchaseOrdersReducer(
      initialState,
      PurchaseOrdersActions.receiveOrderRequested({
        purchaseOrderId: 'po1',
        payload: {
          referenceNumber: null,
          notes: null,
          receivedAt: null,
          lines: [{
            purchaseOrderLineId: 'line-1',
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
    expect(next.submitting).toBe(true);
    expect(next.errorMessage).toBe('');
  });

  it('updates selectedOrder and upserts list entity on receive succeeded', () => {
    const receivedDetail: PurchaseOrderDetail = {
      ...mockDetail,
      status: 'PartiallyReceived',
      receivedQuantity: 1,
      lines: [{ lineId: 'line-1', itemId: 'item-1', description: 'Widget', expectedQuantity: 2, receivedQuantity: 1, remainingQuantity: 1, unitCost: 50, lineTotal: 100 }],
    };
    const next = purchaseOrdersReducer(
      { ...initialState, submitting: true },
      PurchaseOrdersActions.receiveOrderSucceeded({ order: receivedDetail })
    );
    expect(next.submitting).toBe(false);
    expect(next.selectedOrder).toEqual(receivedDetail);
    expect(next.entities['po1']?.status).toBe('PartiallyReceived');
    expect(next.entities['po1']?.receivedQuantity).toBe(1);
  });

  it('sets error on receive failed', () => {
    const next = purchaseOrdersReducer(
      { ...initialState, submitting: true },
      PurchaseOrdersActions.receiveOrderFailed({ errorMessage: 'purchaseOrders.errors.unableToReceive' })
    );
    expect(next.submitting).toBe(false);
    expect(next.errorMessage).toBe('purchaseOrders.errors.unableToReceive');
  });

  it('preserves supplierName, supplierReference, and receivedQuantity in list entity after update/place/cancel', () => {
    const detail: PurchaseOrderDetail = {
      ...mockDetail,
      supplierName: 'Acme Traders',
      supplierReference: 'SUP-REF-001',
      receivedQuantity: 5,
    };
    
    // Test update draft
    const state1 = purchaseOrdersReducer(
      initialState,
      PurchaseOrdersActions.updateDraftSucceeded({ order: detail })
    );
    const item1 = state1.entities['po1'];
    expect(item1?.supplierName).toBe('Acme Traders');
    expect(item1?.supplierReference).toBe('SUP-REF-001');
    expect(item1?.receivedQuantity).toBe(5);

    // Test place order
    const state2 = purchaseOrdersReducer(
      initialState,
      PurchaseOrdersActions.placeOrderSucceeded({ order: { ...detail, status: 'Placed' } })
    );
    const item2 = state2.entities['po1'];
    expect(item2?.supplierName).toBe('Acme Traders');
    expect(item2?.supplierReference).toBe('SUP-REF-001');
    expect(item2?.receivedQuantity).toBe(5);

    // Test cancel order
    const state3 = purchaseOrdersReducer(
      initialState,
      PurchaseOrdersActions.cancelOrderSucceeded({ order: { ...detail, status: 'Cancelled', cancellationReason: 'reason' } })
    );
    const item3 = state3.entities['po1'];
    expect(item3?.supplierName).toBe('Acme Traders');
    expect(item3?.supplierReference).toBe('SUP-REF-001');
    expect(item3?.receivedQuantity).toBe(5);
  });
});
