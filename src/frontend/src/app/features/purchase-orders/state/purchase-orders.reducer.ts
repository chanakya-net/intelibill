import { createEntityAdapter, EntityState } from '@ngrx/entity';
import { createFeature, createReducer, on } from '@ngrx/store';

import {
  DEFAULT_PURCHASE_ORDER_LIST_FILTERS,
  PurchaseOrderDetail,
  PurchaseOrderListFilters,
  PurchaseOrderListItem,
} from '../services/purchase-order.service';
import { PurchaseOrdersActions } from './purchase-orders.actions';

export const purchaseOrdersFeatureKey = 'purchaseOrders';

export const purchaseOrdersAdapter = createEntityAdapter<PurchaseOrderListItem>({
  selectId: (order) => order.purchaseOrderId,
});

export interface PurchaseOrdersState extends EntityState<PurchaseOrderListItem> {
  readonly loadingList: boolean;
  readonly loadingDetail: boolean;
  readonly submitting: boolean;
  readonly errorMessage: string;
  readonly selectedOrder: PurchaseOrderDetail | null;
  readonly createSucceeded: boolean;
  readonly totalCount: number;
  readonly currentPage: number;
  readonly pageSize: number;
  readonly filters: PurchaseOrderListFilters;
}

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

function toListItem(order: PurchaseOrderDetail): PurchaseOrderListItem {
  return {
    purchaseOrderId: order.purchaseOrderId,
    purchaseOrderNumber: order.purchaseOrderNumber,
    status: order.status,
    supplierName: order.supplierName,
    supplierReference: order.supplierReference || order.supplierReferenceNumber,
    lineCount: order.lines.length,
    expectedQuantity: order.lines.reduce((total, line) => total + line.expectedQuantity, 0),
    receivedQuantity: order.receivedQuantity,
    expectedTotal: order.expectedTotal,
    createdAt: order.createdAt,
  };
}

export const purchaseOrdersReducer = createReducer(
  initialState,

  on(PurchaseOrdersActions.loadPurchaseOrdersRequested, (state, { filters }) => ({
    ...state,
    loadingList: true,
    errorMessage: '',
    filters: {
      ...state.filters,
      ...filters,
    },
  })),
  on(PurchaseOrdersActions.loadPurchaseOrdersSucceeded, (state, { result }) =>
    purchaseOrdersAdapter.setAll([...result.items], {
      ...state,
      loadingList: false,
      errorMessage: '',
      totalCount: result.totalCount,
      currentPage: result.pageNumber,
      pageSize: result.pageSize,
      filters: {
        ...state.filters,
        page: result.pageNumber,
        pageSize: result.pageSize,
      },
    })
  ),
  on(PurchaseOrdersActions.loadPurchaseOrdersFailed, (state, { errorMessage }) => ({
    ...state,
    loadingList: false,
    errorMessage,
  })),

  on(PurchaseOrdersActions.loadPurchaseOrderDetailRequested, (state) => ({
    ...state,
    loadingDetail: true,
    selectedOrder: null,
    errorMessage: '',
  })),
  on(PurchaseOrdersActions.loadPurchaseOrderDetailSucceeded, (state, { order }) => ({
    ...state,
    loadingDetail: false,
    selectedOrder: order,
  })),
  on(PurchaseOrdersActions.loadPurchaseOrderDetailFailed, (state, { errorMessage }) => ({
    ...state,
    loadingDetail: false,
    errorMessage,
  })),

  on(PurchaseOrdersActions.createDraftRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    createSucceeded: false,
  })),
  on(PurchaseOrdersActions.createDraftSucceeded, (state, { order }) => ({
    ...state,
    submitting: false,
    createSucceeded: true,
    selectedOrder: order,
  })),
  on(PurchaseOrdersActions.createDraftFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    createSucceeded: false,
  })),

  on(PurchaseOrdersActions.updateDraftRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
  })),
  on(PurchaseOrdersActions.updateDraftSucceeded, (state, { order }) =>
    purchaseOrdersAdapter.upsertOne(toListItem(order), {
      ...state,
      submitting: false,
      selectedOrder: order,
    })
  ),
  on(PurchaseOrdersActions.updateDraftFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
  })),

  on(PurchaseOrdersActions.placeOrderRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
  })),
  on(PurchaseOrdersActions.placeOrderSucceeded, (state, { order }) =>
    purchaseOrdersAdapter.upsertOne(toListItem(order), {
      ...state,
      submitting: false,
      selectedOrder: order,
    })
  ),
  on(PurchaseOrdersActions.placeOrderFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
  })),

  on(PurchaseOrdersActions.deleteDraftRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
  })),
  on(PurchaseOrdersActions.deleteDraftSucceeded, (state, { purchaseOrderId }) =>
    purchaseOrdersAdapter.removeOne(purchaseOrderId, {
      ...state,
      submitting: false,
      selectedOrder: null,
    })
  ),
  on(PurchaseOrdersActions.deleteDraftFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
  })),

  on(PurchaseOrdersActions.cancelOrderRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
  })),
  on(PurchaseOrdersActions.cancelOrderSucceeded, (state, { order }) =>
    purchaseOrdersAdapter.upsertOne(toListItem(order), {
      ...state,
      submitting: false,
      selectedOrder: order,
    })
  ),
  on(PurchaseOrdersActions.cancelOrderFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
  })),

  on(PurchaseOrdersActions.clearDetail, (state) => ({
    ...state,
    selectedOrder: null,
    loadingDetail: false,
  })),
  on(PurchaseOrdersActions.clearError, (state) => ({
    ...state,
    errorMessage: '',
  })),
  on(PurchaseOrdersActions.resetListFilters, (state) => ({
    ...state,
    filters: DEFAULT_PURCHASE_ORDER_LIST_FILTERS,
  }))
);

export const purchaseOrdersFeature = createFeature({
  name: purchaseOrdersFeatureKey,
  reducer: purchaseOrdersReducer,
});
