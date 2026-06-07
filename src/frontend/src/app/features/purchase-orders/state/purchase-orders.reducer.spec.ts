import { purchaseOrdersReducer, PurchaseOrdersState, purchaseOrdersAdapter } from './purchase-orders.reducer';
import { PurchaseOrdersActions } from './purchase-orders.actions';
import { PurchaseOrderListItem, PurchaseOrderDetail } from '../services/purchase-order.service';

const initialState: PurchaseOrdersState = purchaseOrdersAdapter.getInitialState({
  loadingList: false,
  loadingDetail: false,
  submitting: false,
  errorMessage: '',
  selectedOrder: null,
  createSucceeded: false,
});

const mockListItem: PurchaseOrderListItem = {
  purchaseOrderId: 'po1',
  purchaseOrderNumber: 'PO-2026-000001',
  status: 'Draft',
  lineCount: 1,
  expectedTotal: 100,
  createdAt: '2026-06-01T00:00:00Z',
};

const mockDetail: PurchaseOrderDetail = {
  purchaseOrderId: 'po1',
  purchaseOrderNumber: 'PO-2026-000001',
  status: 'Draft',
  supplierId: null,
  orderDate: null,
  expectedDeliveryDate: null,
  supplierReferenceNumber: null,
  notes: null,
  lines: [],
  expectedTotal: 0,
  createdAt: '2026-06-01T00:00:00Z',
};

describe('purchaseOrdersReducer', () => {
  it('sets loadingList on load requested', () => {
    const next = purchaseOrdersReducer(
      initialState,
      PurchaseOrdersActions.loadPurchaseOrdersRequested()
    );
    expect(next.loadingList).toBe(true);
    expect(next.errorMessage).toBe('');
  });

  it('populates list on load succeeded', () => {
    const next = purchaseOrdersReducer(
      { ...initialState, loadingList: true },
      PurchaseOrdersActions.loadPurchaseOrdersSucceeded({ orders: [mockListItem] })
    );
    expect(next.loadingList).toBe(false);
    const ids = next.ids as string[];
    expect(ids).toContain('po1');
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
});
