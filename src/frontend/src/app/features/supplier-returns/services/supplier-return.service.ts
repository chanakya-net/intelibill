import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { API_BASE_URL, SUPPLIER_RETURN_ENDPOINTS as E } from '../../../core/auth/auth.constants';
import { scopedRequestContext } from '../../../core/context/scoped-request';
import type { ScopeOwner } from '../../../core/context/shop-scope';
import type {
  SupplierReturnRequest,
  SupplierReturnCreditRequest,
  ReceiptReversalRequest,
  GroupReceiptReversalRequest,
  SupplierReturnDetail,
  PurchaseReceiptDetail,
  ReceiptReversalResult,
} from './supplier-return.models';
@Injectable({ providedIn: 'root' })
export class SupplierReturnService {
  private readonly http = inject(HttpClient);
  preview(
    owner: ScopeOwner,
    supplierId: string,
    lines: { receiptValueId: string; quantity: number }[],
  ) {
    return this.http.post<{ lines: PurchaseReceiptDetail[] }>(
      E.preview,
      { supplierId, lines },
      { context: scopedRequestContext(owner) },
    );
  }
  record(owner: ScopeOwner, payload: SupplierReturnRequest) {
    return this.http.post<SupplierReturnDetail>(E.list, payload, {
      context: scopedRequestContext(owner),
    });
  }
  acceptCredit(owner: ScopeOwner, id: string, payload: SupplierReturnCreditRequest) {
    return this.http.post<SupplierReturnDetail>(E.credit(id), payload, {
      context: scopedRequestContext(owner),
    });
  }
  reverseReceipt(owner: ScopeOwner, id: string, payload: ReceiptReversalRequest) {
    return this.http.post<ReceiptReversalResult>(E.reversal(id), payload, {
      context: scopedRequestContext(owner),
    });
  }
  reversePurchaseOrderReceipt(owner: ScopeOwner, id: string, payload: GroupReceiptReversalRequest) {
    return this.http.post<ReceiptReversalResult>(E.groupReversal(id), payload, {
      context: scopedRequestContext(owner),
    });
  }
  getReceipt(id: string) {
    return this.http.get<PurchaseReceiptDetail>(E.receipt(id));
  }
  getReturn(id: string) {
    return this.http.get<SupplierReturnDetail>(E.detail(id));
  }
  listReturns(filters: Record<string, string> = {}) {
    return this.http.get<SupplierReturnDetail[]>(E.list, {
      params: new HttpParams({ fromObject: filters }),
    });
  }
  getOperation(
    owner: ScopeOwner,
    kind: 'supplier-return' | 'supplier-return-credit' | 'purchase-receipt-reversal',
    key: string,
  ) {
    return this.http.get<{ response: SupplierReturnDetail | ReceiptReversalResult }>(
      `${API_BASE_URL}/financial-operations/${kind}/${encodeURIComponent(key)}`,
      { context: scopedRequestContext(owner) },
    );
  }
}
