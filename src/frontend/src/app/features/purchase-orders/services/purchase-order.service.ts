import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import { PURCHASE_ORDER_ENDPOINTS } from '../../../core/auth/auth.constants';

export type PurchaseOrderStatus = 'Draft' | 'Placed' | 'Cancelled' | 'PartiallyReceived' | 'Received' | 'Closed';

export interface PurchaseOrderLine {
  readonly lineId: string;
  readonly itemId: string;
  readonly description: string;
  readonly expectedQuantity: number;
  readonly receivedQuantity?: number;
  readonly remainingQuantity?: number;
  readonly unitCost: number;
  readonly lineTotal: number;
}

export interface PurchaseOrderReceiptLine {
  readonly receiptLineId: string;
  readonly purchaseOrderLineId: string;
  readonly itemId: string;
  readonly inventoryBatchId: string;
  readonly batchNumber?: string | null;
  readonly batchVoided?: boolean | null;
  readonly stockTransactionId: string;
  readonly quantity: number;
  readonly totalPurchaseCost: number;
  readonly unitCost: number;
  readonly mrp?: number;
  readonly salesPrice?: number;
  readonly taxRatePercent?: number;
  readonly taxIncluded?: boolean;
  readonly purchaseTaxIncluded?: boolean;
}

export interface PurchaseOrderReceipt {
  readonly receiptId: string;
  readonly receiptNumber: string;
  readonly receivedAt: string;
  readonly referenceNumber: string | null;
  readonly notes: string | null;
  readonly receivedByUserId?: string;
  readonly receivedByDisplayName?: string | null;
  readonly lines: readonly PurchaseOrderReceiptLine[];
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
  readonly supplierId: string | null;
  readonly supplierName: string | null;
  readonly supplierReference: string | null;
  readonly receivedQuantity: number;
  readonly orderDate: string | null;
  readonly expectedDeliveryDate: string | null;
  readonly supplierReferenceNumber: string | null;
  readonly notes: string | null;
  readonly lines: readonly PurchaseOrderLine[];
  readonly expectedTotal: number;
  readonly createdAt: string;
  readonly cancellationReason: string | null;
  readonly receipts?: readonly PurchaseOrderReceipt[];
  readonly closedAt?: string | null;
  readonly closedBy?: string | null;
  readonly closeReason?: string | null;
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
  readonly supplierName?: string | null;
  readonly supplierReference?: string | null;
  readonly lines: readonly CreatePurchaseOrderLineRequest[];
}

export interface CancelPurchaseOrderRequest {
  readonly reason: string | null;
}

export interface ClosePurchaseOrderRequest {
  readonly reason: string | null;
}

export interface ReceivePurchaseOrderLineRequest {
  readonly purchaseOrderLineId: string;
  readonly barcode: string;
  readonly batchNumber: string;
  readonly quantity: number;
  readonly totalPurchaseCost: number;
  readonly mrp: number;
  readonly salesPrice: number;
  readonly taxRatePercent: number;
  readonly taxIncluded: boolean;
  readonly purchaseTaxIncluded: boolean;
  readonly expiryDate: string | null;
  readonly manufacturingDate: string | null;
}

export interface ReceivePurchaseOrderRequest {
  readonly referenceNumber: string | null;
  readonly notes: string | null;
  readonly receivedAt: string | null;
  readonly lines: readonly ReceivePurchaseOrderLineRequest[];
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

  updateDraft(purchaseOrderId: string, payload: CreatePurchaseOrderDraftRequest): Observable<PurchaseOrderDetail> {
    return this.http.put<PurchaseOrderDetail>(PURCHASE_ORDER_ENDPOINTS.detail(purchaseOrderId), payload);
  }

  placePurchaseOrder(purchaseOrderId: string): Observable<PurchaseOrderDetail> {
    return this.http.post<PurchaseOrderDetail>(PURCHASE_ORDER_ENDPOINTS.place(purchaseOrderId), {});
  }

  deleteDraft(purchaseOrderId: string): Observable<void> {
    return this.http.delete<void>(PURCHASE_ORDER_ENDPOINTS.delete(purchaseOrderId));
  }

  cancelPurchaseOrder(purchaseOrderId: string, payload: CancelPurchaseOrderRequest): Observable<PurchaseOrderDetail> {
    return this.http.post<PurchaseOrderDetail>(PURCHASE_ORDER_ENDPOINTS.cancel(purchaseOrderId), payload);
  }

  closePurchaseOrder(purchaseOrderId: string, payload: ClosePurchaseOrderRequest): Observable<PurchaseOrderDetail> {
    return this.http.post<PurchaseOrderDetail>(PURCHASE_ORDER_ENDPOINTS.close(purchaseOrderId), payload);
  }

  receivePurchaseOrder(purchaseOrderId: string, payload: ReceivePurchaseOrderRequest): Observable<PurchaseOrderDetail> {
    return this.http.post<PurchaseOrderDetail>(PURCHASE_ORDER_ENDPOINTS.receipts(purchaseOrderId), payload);
  }
}
