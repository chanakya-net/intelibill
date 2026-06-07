import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import { PURCHASE_ORDER_ENDPOINTS } from '../../../core/auth/auth.constants';

export type PurchaseOrderStatus = 'Draft';

export interface PurchaseOrderLine {
  readonly lineId: string;
  readonly description: string;
  readonly expectedQuantity: number;
  readonly unitCost: number;
  readonly lineTotal: number;
}

export interface PurchaseOrderListItem {
  readonly purchaseOrderId: string;
  readonly purchaseOrderNumber: string;
  readonly status: PurchaseOrderStatus;
  readonly supplierName: string | null;
  readonly supplierReference: string | null;
  readonly lineCount: number;
  readonly expectedQuantity: number;
  readonly receivedQuantity: number;
  readonly expectedTotal: number;
  readonly createdAt: string;
}

export interface PurchaseOrderListFilters {
  readonly search: string;
  readonly status: PurchaseOrderStatus | '';
  readonly orderDateFrom: string;
  readonly orderDateTo: string;
  readonly page: number;
  readonly pageSize: number;
}

export interface PurchaseOrderListResult {
  readonly items: readonly PurchaseOrderListItem[];
  readonly totalCount: number;
  readonly pageNumber: number;
  readonly pageSize: number;
}

export interface PurchaseOrderDetail {
  readonly purchaseOrderId: string;
  readonly purchaseOrderNumber: string;
  readonly status: PurchaseOrderStatus;
  readonly notes: string | null;
  readonly lines: readonly PurchaseOrderLine[];
  readonly expectedTotal: number;
  readonly createdAt: string;
}

export interface CreatePurchaseOrderLineRequest {
  readonly description: string;
  readonly expectedQuantity: number;
  readonly unitCost: number;
}

export interface CreatePurchaseOrderDraftRequest {
  readonly notes: string | null;
  readonly supplierName: string | null;
  readonly supplierReference: string | null;
  readonly lines: readonly CreatePurchaseOrderLineRequest[];
}

export const DEFAULT_PURCHASE_ORDER_LIST_FILTERS: PurchaseOrderListFilters = {
  search: '',
  status: '',
  orderDateFrom: '',
  orderDateTo: '',
  page: 1,
  pageSize: 20,
};

@Injectable({ providedIn: 'root' })
export class PurchaseOrderService {
  private readonly http = inject(HttpClient);

  getPurchaseOrders(filters: Partial<PurchaseOrderListFilters> = {}): Observable<PurchaseOrderListResult> {
    const request = { ...DEFAULT_PURCHASE_ORDER_LIST_FILTERS, ...filters };
    let params = new HttpParams()
      .set('page', request.page.toString())
      .set('page_size', request.pageSize.toString());

    if (request.search.trim()) params = params.set('search', request.search.trim());
    if (request.status) params = params.set('status', request.status);
    if (request.orderDateFrom) params = params.set('order_date_from', request.orderDateFrom);
    if (request.orderDateTo) params = params.set('order_date_to', request.orderDateTo);

    return this.http.get<PurchaseOrderListResult>(PURCHASE_ORDER_ENDPOINTS.list, { params });
  }

  getPurchaseOrderDetail(purchaseOrderId: string): Observable<PurchaseOrderDetail> {
    return this.http.get<PurchaseOrderDetail>(PURCHASE_ORDER_ENDPOINTS.detail(purchaseOrderId));
  }

  createDraft(payload: CreatePurchaseOrderDraftRequest): Observable<PurchaseOrderDetail> {
    return this.http.post<PurchaseOrderDetail>(PURCHASE_ORDER_ENDPOINTS.create, payload);
  }
}
