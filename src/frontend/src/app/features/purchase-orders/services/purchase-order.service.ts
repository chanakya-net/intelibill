import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

import { PURCHASE_ORDER_ENDPOINTS } from '../../../core/auth/auth.constants';

export type PurchaseOrderStatus = 'Draft';

export interface PurchaseOrderLine {
  readonly lineId: string;
  readonly itemId: string;
  readonly description: string;
  readonly expectedQuantity: number;
  readonly unitCost: number;
  readonly lineTotal: number;
}

export interface PurchaseOrderListItem {
  readonly purchaseOrderId: string;
  readonly purchaseOrderNumber: string;
  readonly status: PurchaseOrderStatus;
  readonly lineCount: number;
  readonly expectedTotal: number;
  readonly createdAt: string;
}

export interface PurchaseOrderDetail {
  readonly purchaseOrderId: string;
  readonly purchaseOrderNumber: string;
  readonly status: PurchaseOrderStatus;
  readonly supplierId: string | null;
  readonly orderDate: string | null;
  readonly expectedDeliveryDate: string | null;
  readonly supplierReferenceNumber: string | null;
  readonly notes: string | null;
  readonly lines: readonly PurchaseOrderLine[];
  readonly expectedTotal: number;
  readonly createdAt: string;
}

export interface CreatePurchaseOrderLineRequest {
  readonly itemId: string;
  readonly description: string;
  readonly expectedQuantity: number;
  readonly unitCost: number;
}

export interface CreatePurchaseOrderDraftRequest {
  readonly supplierId: string | null;
  readonly orderDate: string | null;
  readonly expectedDeliveryDate: string | null;
  readonly supplierReferenceNumber: string | null;
  readonly notes: string | null;
  readonly lines: readonly CreatePurchaseOrderLineRequest[];
}

@Injectable({ providedIn: 'root' })
export class PurchaseOrderService {
  private readonly http = inject(HttpClient);

  getPurchaseOrders(page = 1, pageSize = 20): Observable<readonly PurchaseOrderListItem[]> {
    return this.http.get<readonly PurchaseOrderListItem[]>(
      `${PURCHASE_ORDER_ENDPOINTS.list}?page=${page}&page_size=${pageSize}`
    );
  }

  getPurchaseOrderDetail(purchaseOrderId: string): Observable<PurchaseOrderDetail> {
    return this.http.get<PurchaseOrderDetail>(PURCHASE_ORDER_ENDPOINTS.detail(purchaseOrderId));
  }

  createDraft(payload: CreatePurchaseOrderDraftRequest): Observable<PurchaseOrderDetail> {
    return this.http.post<PurchaseOrderDetail>(PURCHASE_ORDER_ENDPOINTS.create, payload);
  }

  updateDraft(purchaseOrderId: string, payload: CreatePurchaseOrderDraftRequest): Observable<PurchaseOrderDetail> {
    return this.http.put<PurchaseOrderDetail>(PURCHASE_ORDER_ENDPOINTS.detail(purchaseOrderId), payload);
  }
}
