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
  notes: null,
  lines: [],
  expectedTotal: 0,
  createdAt: '2026-06-01T00:00:00Z',
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

  it('clears detail on clearDetail', () => {
    const next = purchaseOrdersReducer(
      { ...initialState, selectedOrder: mockDetail },
      PurchaseOrdersActions.clearDetail()
    );
    expect(next.selectedOrder).toBeNull();
  });
});
