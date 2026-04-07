import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { SUPPLIER_ENDPOINTS } from '../../../core/auth/auth.constants';

export interface SupplierLedgerEntry {
  readonly id: string;
  readonly supplierId: string;
  readonly entryType: 'GOODS_RECEIVED' | 'PAYMENT_MADE' | 'RECORD_ADJUSTED' | 1 | 2 | 3;
  readonly amount: number;
  readonly entryDate: string;
  readonly notes: string | null;
}

export interface MakePaymentRequest {
  readonly amount: number;
  readonly paymentDate: string;
  readonly notes: string | null;
}

@Injectable({
  providedIn: 'root',
})
export class SupplierLedgerService {
  private readonly http = inject(HttpClient);

  getSupplierLedgerEntries(supplierId: string): Observable<readonly SupplierLedgerEntry[]> {
    return this.http.get<readonly SupplierLedgerEntry[]>(`${SUPPLIER_ENDPOINTS.update(supplierId)}/ledger`);
  }

  makePayment(supplierId: string, payload: MakePaymentRequest): Observable<SupplierLedgerEntry> {
    return this.http.post<SupplierLedgerEntry>(SUPPLIER_ENDPOINTS.payments(supplierId), payload);
  }
}
