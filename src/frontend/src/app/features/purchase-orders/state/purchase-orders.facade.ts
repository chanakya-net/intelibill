import { Injectable, Signal, inject } from '@angular/core';
import { Store } from '@ngrx/store';

import {
  CancelPurchaseOrderRequest,
  CreatePurchaseOrderDraftRequest,
  DEFAULT_PURCHASE_ORDER_LIST_FILTERS,
  PurchaseOrderDetail,
  PurchaseOrderListFilters,
  PurchaseOrderListItem,
  ReceivePurchaseOrderRequest,
} from '../services/purchase-order.service';
import { PurchaseOrdersActions } from './purchase-orders.actions';
import {
  selectAllPurchaseOrders,
  selectCreateDraftSucceeded,
  selectPurchaseOrdersErrorMessage,
  selectPurchaseOrdersFilters,
  selectPurchaseOrdersLoadingDetail,
  selectPurchaseOrdersLoadingList,
  selectPurchaseOrdersPagination,
  selectPurchaseOrdersSubmitting,
  selectSelectedPurchaseOrder,
} from './purchase-orders.selectors';

@Injectable({ providedIn: 'root' })
export class PurchaseOrdersFacade {
  private readonly store = inject(Store);

  readonly orders: Signal<readonly PurchaseOrderListItem[]> = this.store.selectSignal(selectAllPurchaseOrders);
  readonly isLoadingList: Signal<boolean> = this.store.selectSignal(selectPurchaseOrdersLoadingList);
  readonly isLoadingDetail: Signal<boolean> = this.store.selectSignal(selectPurchaseOrdersLoadingDetail);
  readonly isSubmitting: Signal<boolean> = this.store.selectSignal(selectPurchaseOrdersSubmitting);
  readonly errorMessage: Signal<string> = this.store.selectSignal(selectPurchaseOrdersErrorMessage);
  readonly selectedOrder: Signal<PurchaseOrderDetail | null> = this.store.selectSignal(selectSelectedPurchaseOrder);
  readonly createSucceeded: Signal<boolean> = this.store.selectSignal(selectCreateDraftSucceeded);
  readonly filters: Signal<PurchaseOrderListFilters> = this.store.selectSignal(selectPurchaseOrdersFilters);
  readonly pagination: Signal<{ totalCount: number; pageNumber: number; pageSize: number }> =
    this.store.selectSignal(selectPurchaseOrdersPagination);

  loadOrders(filters: Partial<PurchaseOrderListFilters> = {}): void {
    this.store.dispatch(
      PurchaseOrdersActions.loadPurchaseOrdersRequested({
        filters: { ...DEFAULT_PURCHASE_ORDER_LIST_FILTERS, ...this.filters(), ...filters },
      })
    );
  }

  loadDetail(purchaseOrderId: string): void {
    this.store.dispatch(PurchaseOrdersActions.loadPurchaseOrderDetailRequested({ purchaseOrderId }));
  }

  createDraft(payload: CreatePurchaseOrderDraftRequest): void {
    this.store.dispatch(PurchaseOrdersActions.createDraftRequested({ payload }));
  }

  updateDraft(purchaseOrderId: string, payload: CreatePurchaseOrderDraftRequest): void {
    this.store.dispatch(PurchaseOrdersActions.updateDraftRequested({ purchaseOrderId, payload }));
  }

  placeOrder(purchaseOrderId: string): void {
    this.store.dispatch(PurchaseOrdersActions.placeOrderRequested({ purchaseOrderId }));
  }

  deleteDraft(purchaseOrderId: string): void {
    this.store.dispatch(PurchaseOrdersActions.deleteDraftRequested({ purchaseOrderId }));
  }

  cancelOrder(purchaseOrderId: string, payload: CancelPurchaseOrderRequest): void {
    this.store.dispatch(PurchaseOrdersActions.cancelOrderRequested({ purchaseOrderId, payload }));
  }

  receiveOrder(purchaseOrderId: string, payload: ReceivePurchaseOrderRequest): void {
    this.store.dispatch(PurchaseOrdersActions.receiveOrderRequested({ purchaseOrderId, payload }));
  }

  clearDetail(): void {
    this.store.dispatch(PurchaseOrdersActions.clearDetail());
  }

  clearError(): void {
    this.store.dispatch(PurchaseOrdersActions.clearError());
  }

  resetListFilters(): void {
    this.store.dispatch(PurchaseOrdersActions.resetListFilters());
  }
}
