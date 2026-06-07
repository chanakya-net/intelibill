import { createEntityAdapter, EntityState } from '@ngrx/entity';
import { createFeature, createReducer, on } from '@ngrx/store';

import { PurchaseOrderDetail, PurchaseOrderListItem } from '../services/purchase-order.service';
import { PurchaseOrdersActions } from './purchase-orders.actions';

export const purchaseOrdersFeatureKey = 'purchaseOrders';

export const purchaseOrdersAdapter = createEntityAdapter<PurchaseOrderListItem>({
  selectId: (order) => order.purchaseOrderId,
  sortComparer: (a, b) =>
    new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
});

export interface PurchaseOrdersState extends EntityState<PurchaseOrderListItem> {
  readonly loadingList: boolean;
  readonly loadingDetail: boolean;
  readonly submitting: boolean;
  readonly errorMessage: string;
  readonly selectedOrder: PurchaseOrderDetail | null;
  readonly createSucceeded: boolean;
}

const initialState: PurchaseOrdersState = purchaseOrdersAdapter.getInitialState({
  loadingList: false,
  loadingDetail: false,
  submitting: false,
  errorMessage: '',
  selectedOrder: null,
  createSucceeded: false,
});

export const purchaseOrdersReducer = createReducer(
  initialState,

  on(PurchaseOrdersActions.loadPurchaseOrdersRequested, (state) => ({
    ...state,
    loadingList: true,
    errorMessage: '',
  })),
  on(PurchaseOrdersActions.loadPurchaseOrdersSucceeded, (state, { orders }) =>
    purchaseOrdersAdapter.setAll([...orders], {
      ...state,
      loadingList: false,
      errorMessage: '',
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

  on(PurchaseOrdersActions.clearDetail, (state) => ({
    ...state,
    selectedOrder: null,
    loadingDetail: false,
  })),
  on(PurchaseOrdersActions.clearError, (state) => ({
    ...state,
    errorMessage: '',
  }))
);

export const purchaseOrdersFeature = createFeature({
  name: purchaseOrdersFeatureKey,
  reducer: purchaseOrdersReducer,
});
